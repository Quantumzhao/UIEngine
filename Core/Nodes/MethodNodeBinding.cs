using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace UIEngine.Core;

internal sealed class ReflectedAction
{
    private ReflectedAction(
        MethodInfo method,
        ParameterInfo[] userParameters,
        Type? resultType)
    {
        Method = method;
        MethodParameters = method.GetParameters();
        UserParameters = userParameters;
        ResultType = resultType;
    }

    public MethodInfo Method { get; }

    public ParameterInfo[] MethodParameters { get; }

    public ParameterInfo[] UserParameters { get; }

    public Type? ResultType { get; }

    public bool IsAsynchronous => typeof(Task).IsAssignableFrom(Method.ReturnType);

    public static InteractionResult<ReflectedAction> Create(MethodInfo method)
    {
        if (method.IsStatic || method.IsGenericMethodDefinition || method.ContainsGenericParameters)
        {
            return _Unsupported(method, "actions must be non-generic instance methods");
        }

        var parameters = method.GetParameters();
        if (parameters.Any(static parameter => parameter.ParameterType.IsByRef || parameter.IsOut))
        {
            return _Unsupported(method, "ref and out parameters are not supported");
        }

        var returnType = method.ReturnType;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>) ||
            returnType == typeof(ValueTask))
        {
            return _Unsupported(method, "ValueTask actions are not supported");
        }

        Type? resultType;
        if (returnType == typeof(void) || returnType == typeof(Task))
        {
            resultType = null;
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            resultType = returnType.GetGenericArguments()[0];
        }
        else if (typeof(Task).IsAssignableFrom(returnType))
        {
            return _Unsupported(method, "custom Task subclasses are not supported");
        }
        else
        {
            resultType = returnType;
        }

        return InteractionResult.Success(new ReflectedAction(
            method,
            parameters,
            resultType));
    }

    private static InteractionResult<ReflectedAction> _Unsupported(MethodInfo method, string reason) =>
        InteractionResult.Failure<ReflectedAction>(
            InteractionErrorCode.UNSUPPORTED,
            $"Action '{method.Name}' is unsupported because {reason}.");
}

internal sealed class MethodNodeBinding
{
    private readonly UIEngineHost _Host;
    private readonly Guid _Owner;
    private readonly ReflectedAction _Action;
    private readonly List<IReadOnlyList<ValidationAttribute>> _ValidationAttributes;
    private readonly object _InvocationGate = new();
    private ActionInvocation? _CurrentInvocation;

    public MethodNodeBinding(
        UIEngineHost host,
        Guid owner,
        string id,
        ReflectedAction action)
    {
        _Host = host;
        _Owner = owner;
        _Action = action;
        Id = id;

        var validation = new List<IReadOnlyList<ValidationAttribute>>();
        var parameters = new List<MethodParameter>();
        foreach (var parameter in action.UserParameters)
        {
            var attributes = ReflectionMetadata.GetValidationAttributes(parameter);
            validation.Add(attributes);
            var options = ValueConversion.GetEnumOptions(parameter.ParameterType);
            var range = attributes.OfType<RangeAttribute>()
                .Select(static attribute =>
                    new ValueRange<object>(attribute.Minimum, attribute.Maximum))
                .FirstOrDefault();
            parameters.Add(new MethodParameter(
                parameter.Name ?? $"arg{parameter.Position}",
                parameter.ParameterType,
                !parameter.IsOptional,
                ReflectionMetadata.IsNullable(parameter) &&
                    attributes.All(static attribute => attribute is not RequiredAttribute),
                parameter.HasDefaultValue,
                parameter.HasDefaultValue ? parameter.DefaultValue : null,
                options,
                range));
        }

        Parameters = parameters;
        _ValidationAttributes = validation;
    }

    public UIEngineHost Host => _Host;

    public string Id { get; }

    public Type ReturnType => _Action.Method.ReturnType;

    public IReadOnlyList<MethodParameter> Parameters { get; }

    public Type? ResultType => _Action.ResultType;

    public bool IsAsynchronous => _Action.IsAsynchronous;

    public InvocationStatus? Status
    {
        get
        {
            lock (_InvocationGate)
            {
                return _CurrentInvocation?.Status;
            }
        }
    }

    public InteractionResult<ActionInvocation> Invoke(
        IReadOnlyDictionary<string, object?> arguments) => _Host.Execute(
        $"invoke action {Id}",
        () =>
        {
            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<ActionInvocation>(target.Error!);
            }

            var unknown = arguments.Keys.FirstOrDefault(name =>
                Parameters.All(parameter => !StringComparer.Ordinal.Equals(parameter.Id, name)));
            if (unknown is not null)
            {
                return InteractionResult.Failure<ActionInvocation>(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Action '{Id}' has no parameter named '{unknown}'.");
            }

            var bound = new object?[_Action.MethodParameters.Length];
            for (var index = 0; index < Parameters.Count; index++)
            {
                var metadata = Parameters[index];
                var parameter = _Action.UserParameters[index];
                if (!arguments.TryGetValue(metadata.Id, out var supplied))
                {
                    if (metadata.IsRequired)
                    {
                        var message = $"Required parameter '{metadata.Id}' was not supplied.";
                        return InteractionResult.Failure<ActionInvocation>(
                            InteractionErrorCode.INVALID_INPUT,
                            message,
                            [new ValidationIssue(
                                ValidationIssueCode.REQUIRED,
                                metadata.Id,
                                message)]);
                    }

                    bound[parameter.Position] = metadata.HasDefaultValue
                        ? metadata.DefaultValue
                        : null;
                    continue;
                }

                var converted = ValueConversion.Convert(supplied, metadata.ParameterType);
                if (!converted.IsSuccess)
                {
                    return InteractionResult.Failure<ActionInvocation>(converted.Error!);
                }

                var issues = ValueConversion.Validate(
                    converted.Value,
                    metadata.IsNullable,
                    metadata.Options,
                    metadata.Range,
                    _ValidationAttributes[index],
                    target.Value,
                    metadata.Id);
                if (issues.Count > 0)
                {
                    return InteractionResult.Failure<ActionInvocation>(
                        InteractionErrorCode.VALIDATION_FAILED,
                        $"Parameter '{metadata.Id}' failed validation.",
                        issues);
                }

                bound[parameter.Position] = converted.Value;
            }

            var invocation = _Host.CreateInvocation();
            lock (_InvocationGate)
            {
                _CurrentInvocation = invocation;
            }

            try
            {
                var returned = _Action.Method.Invoke(target.Value, bound);
                if (_Action.IsAsynchronous)
                {
                    _ = _CompleteAsync(invocation, (Task)returned!);
                }
                else
                {
                    invocation.CompleteSuccess(returned);
                }
            }
            catch (TargetInvocationException exception)
            {
                invocation.CompleteFailure(exception.InnerException ?? exception);
            }
            catch (Exception exception)
            {
                invocation.CompleteFailure(exception);
            }

            return InteractionResult.Success(invocation);
        });

    private async Task _CompleteAsync(ActionInvocation invocation, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            var result = ResultType is null
                ? null
                : task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
            invocation.CompleteSuccess(result);
        }
        catch (Exception exception)
        {
            invocation.CompleteFailure(exception);
        }
    }
}
