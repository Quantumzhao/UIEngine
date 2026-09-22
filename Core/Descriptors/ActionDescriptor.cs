using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace UIEngine.Core;

internal sealed class ReflectedAction
{
    private ReflectedAction(
        MethodInfo method,
        ParameterInfo[] userParameters,
        ParameterInfo? progressParameter,
        Type? resultType,
        Type? progressType)
    {
        Method = method;
        MethodParameters = method.GetParameters();
        UserParameters = userParameters;
        ProgressParameter = progressParameter;
        ResultType = resultType;
        ProgressType = progressType;
    }

    public MethodInfo Method { get; }

    public ParameterInfo[] MethodParameters { get; }

    public ParameterInfo[] UserParameters { get; }

    public ParameterInfo? ProgressParameter { get; }

    public Type? ResultType { get; }

    public Type? ProgressType { get; }

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

        var infrastructureStart = Array.FindIndex(parameters, static parameter =>
            _IsProgress(parameter.ParameterType));
        if (infrastructureStart < 0)
        {
            infrastructureStart = parameters.Length;
        }

        if (parameters.Skip(infrastructureStart).Any(static parameter =>
                !_IsProgress(parameter.ParameterType)))
        {
            return _Unsupported(method, "user parameters must precede the progress parameter");
        }

        var progressParameters = parameters.Where(
            static parameter => _IsProgress(parameter.ParameterType)).ToArray();
        if (progressParameters.Length > 1)
        {
            return _Unsupported(method, "only one progress parameter is supported");
        }

        var progress = progressParameters.SingleOrDefault();

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
            parameters.Take(infrastructureStart).ToArray(),
            progress,
            resultType,
            progress?.ParameterType.GetGenericArguments()[0]));
    }

    private static bool _IsProgress(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IProgress<>);

    private static InteractionResult<ReflectedAction> _Unsupported(MethodInfo method, string reason) =>
        InteractionResult.Failure<ReflectedAction>(
            InteractionErrorCode.UNSUPPORTED,
            $"Action '{method.Name}' is unsupported because {reason}.");
}

public sealed class ActionDescriptor : MemberDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly ObjectHandle _Owner;
    private readonly ReflectedAction _Action;
    private readonly List<IReadOnlyList<ValidationAttribute>> _ValidationAttributes;

    internal ActionDescriptor(
        UIEngineHost host,
        ObjectHandle owner,
        string id,
        ReflectedAction action)
        : base(host, owner, id, MemberKind.ACTION)
    {
        _Host = host;
        _Owner = owner;
        _Action = action;
        var validation = new List<IReadOnlyList<ValidationAttribute>>();
        var parameters = new List<ActionParameter>();
        foreach (var parameter in action.UserParameters)
        {
            var attributes = ReflectionMetadata.GetValidationAttributes(parameter);
            validation.Add(attributes);
            var options = ValueConversion.GetEnumOptions(parameter.ParameterType);
            var range = attributes.OfType<RangeAttribute>()
                .Select(static attribute =>
                    new ValueRange<object>(attribute.Minimum, attribute.Maximum))
                .FirstOrDefault();
            parameters.Add(new ActionParameter(
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

    public IReadOnlyList<ActionParameter> Parameters { get; }

    public Type? ResultType => _Action.ResultType;

    public bool IsAsynchronous => _Action.IsAsynchronous;

    public Type? ProgressType => _Action.ProgressType;

    internal Type ReturnType => _Action.Method.ReturnType;

    public Task<InteractionResult<ActionInvocation>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments) => _Host.ExecuteAsync(
        $"invoke action {Id}",
        async () =>
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
                var descriptor = Parameters[index];
                var parameter = _Action.UserParameters[index];
                if (!arguments.TryGetValue(descriptor.Id, out var supplied))
                {
                    if (descriptor.IsRequired)
                    {
                        var message = $"Required parameter '{descriptor.Id}' was not supplied.";
                        return InteractionResult.Failure<ActionInvocation>(
                            InteractionErrorCode.INVALID_INPUT,
                            message,
                            [new ValidationIssue(
                                ValidationIssueCode.REQUIRED,
                                descriptor.Id,
                                message)]);
                    }

                    bound[parameter.Position] = descriptor.HasDefaultValue
                        ? descriptor.DefaultValue
                        : null;
                    continue;
                }

                var converted = ValueConversion.Convert(supplied, descriptor.ParameterType);
                if (!converted.IsSuccess)
                {
                    return InteractionResult.Failure<ActionInvocation>(converted.Error!);
                }

                var issues = ValueConversion.Validate(
                    converted.Value,
                    descriptor.IsNullable,
                    descriptor.Options,
                    descriptor.Range,
                    _ValidationAttributes[index],
                    target.Value,
                    descriptor.Id);
                if (issues.Count > 0)
                {
                    return InteractionResult.Failure<ActionInvocation>(
                        InteractionErrorCode.VALIDATION_FAILED,
                        $"Parameter '{descriptor.Id}' failed validation.",
                        issues);
                }

                bound[parameter.Position] = converted.Value;
            }

            var invocation = _Host.CreateInvocation(Id);

            if (_Action.ProgressParameter is not null)
            {
                bound[_Action.ProgressParameter.Position] =
                    invocation.CreateProgressReporter(_Action.ProgressType!);
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

            await Task.CompletedTask;
            return InteractionResult.Success(invocation);
        });

    private async Task _CompleteAsync(ActionInvocation invocation, Task task)
    {
        try
        {
            await task;
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
