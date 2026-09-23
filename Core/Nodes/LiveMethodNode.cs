using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace UIEngine.Core;

internal sealed class LiveMethodNode : LiveReflectedMemberNode, IMethodNode
{
    private readonly Guid _Owner;
    private readonly MethodInfo _Method;
    private readonly List<IReadOnlyList<ValidationAttribute>> _ValidationAttributes;
    private readonly Lock _InvocationGate = new();
    private Task<InteractionResult<object?>>? _ResultTask;

    public static InteractionResult<LiveMethodNode> Create(
        UIEngineHost host,
        Guid owner,
        ReflectedMember member)
    {
        var method = (MethodInfo)member.Member;
        if (method.IsStatic || method.ContainsGenericParameters)
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

        return InteractionResult.Success(new LiveMethodNode(
            host, owner, member, method, parameters, resultType));
    }

    private static InteractionResult<LiveMethodNode> _Unsupported(MethodInfo method, string reason) =>
        InteractionResult.Failure<LiveMethodNode>(
            InteractionErrorCode.UNSUPPORTED,
            $"Action '{method.Name}' is unsupported because {reason}.");

    private LiveMethodNode(
        UIEngineHost host,
        Guid owner,
        ReflectedMember member,
        MethodInfo method,
        ParameterInfo[] methodParameters,
        Type? resultType)
        : base(host, member.Name, member, method.ReturnType)
    {
        _Owner = owner;
        _Method = method;
        ResultType = resultType;

        var validation = new List<IReadOnlyList<ValidationAttribute>>();
        var parameters = new List<MethodParameter>();
        foreach (var parameter in methodParameters)
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

    public IReadOnlyList<MethodParameter> Parameters { get; }

    public Type? ResultType { get; }

    public bool IsAsynchronous => typeof(Task).IsAssignableFrom(_Method.ReturnType);

    public InvocationStatus? Status
    {
        get
        {
            var resultTask = ResultTask;
            return resultTask is null ? null : _GetStatus(resultTask);
        }
    }

    public Task<InteractionResult<object?>>? ResultTask
    {
        get
        {
            lock (_InvocationGate)
            {
                return _ResultTask;
            }
        }
    }

    public InteractionResult<InvocationStatus> Invoke(
        IReadOnlyDictionary<string, object?> arguments) => Host.Execute(
        $"invoke action {Name}",
        () =>
        {
            lock (_InvocationGate)
            {
                if (_ResultTask is { IsCompleted: false })
                {
                    return _AlreadyRunning();
                }
            }

            var target = Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<InvocationStatus>(target.Error!);
            }

            var unknown = arguments.Keys.FirstOrDefault(name =>
                Parameters.All(parameter => !StringComparer.Ordinal.Equals(parameter.Name, name)));
            if (unknown is not null)
            {
                return InteractionResult.Failure<InvocationStatus>(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Action '{Name}' has no parameter named '{unknown}'.");
            }

            var bound = new object?[Parameters.Count];
            for (var index = 0; index < Parameters.Count; index++)
            {
                var metadata = Parameters[index];
                if (!arguments.TryGetValue(metadata.Name, out var supplied))
                {
                    if (metadata.IsRequired)
                    {
                        var message = $"Required parameter '{metadata.Name}' was not supplied.";
                        return InteractionResult.Failure<InvocationStatus>(
                            InteractionErrorCode.INVALID_INPUT,
                            message,
                            [new ValidationIssue(
                                ValidationIssueCode.REQUIRED,
                                metadata.Name,
                                message)]);
                    }

                    bound[index] = metadata.HasDefaultValue
                        ? metadata.DefaultValue
                        : null;
                    continue;
                }

                var converted = ValueConversion.Convert(supplied, metadata.ParameterType);
                if (!converted.IsSuccess)
                {
                    return InteractionResult.Failure<InvocationStatus>(converted.Error!);
                }

                var issues = ValueConversion.Validate(
                    converted.Value,
                    metadata.IsNullable,
                    metadata.Options,
                    metadata.Range,
                    _ValidationAttributes[index],
                    target.Value,
                    metadata.Name);
                if (issues.Count > 0)
                {
                    return InteractionResult.Failure<InvocationStatus>(
                        InteractionErrorCode.VALIDATION_FAILED,
                        $"Parameter '{metadata.Name}' failed validation.",
                        issues);
                }

                bound[index] = converted.Value;
            }

            TaskCompletionSource<InteractionResult<object?>> completion;
            lock (_InvocationGate)
            {
                if (_ResultTask is { IsCompleted: false })
                {
                    return _AlreadyRunning();
                }

                completion = new TaskCompletionSource<InteractionResult<object?>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _ResultTask = completion.Task;
            }

            try
            {
                var returned = _Method.Invoke(target.Value, bound);
                if (IsAsynchronous)
                {
                    if (returned is Task task)
                    {
                        _ = _CompleteAsync(completion, task);
                    }
                    else
                    {
                        _CompleteFailure(
                            completion,
                            new InvalidOperationException(
                                $"Action '{Name}' returned a null task."));
                    }
                }
                else
                {
                    completion.TrySetResult(InteractionResult.Success(returned));
                }
            }
            catch (TargetInvocationException exception)
            {
                _CompleteFailure(completion, exception.InnerException ?? exception);
            }
            catch (Exception exception)
            {
                _CompleteFailure(completion, exception);
            }

            return InteractionResult.Success(_GetStatus(completion.Task));
        });

    private static InvocationStatus _GetStatus(Task<InteractionResult<object?>> resultTask)
    {
        if (!resultTask.IsCompletedSuccessfully) return InvocationStatus.RUNNING;
        return resultTask.Result.IsSuccess
            ? InvocationStatus.SUCCEEDED
            : InvocationStatus.FAILED;
    }

    private async Task _CompleteAsync(
        TaskCompletionSource<InteractionResult<object?>> completion,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            var result = ResultType is null
                ? null
                : task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
            completion.TrySetResult(InteractionResult.Success(result));
        }
        catch (Exception exception)
        {
            _CompleteFailure(completion, exception);
        }
    }

    private InteractionResult<InvocationStatus> _AlreadyRunning() =>
        InteractionResult.Failure<InvocationStatus>(
            InteractionErrorCode.UNAVAILABLE,
            $"Action '{Name}' is already running on this method node.");

    private void _CompleteFailure(
        TaskCompletionSource<InteractionResult<object?>> completion,
        Exception exception)
    {
        completion.TrySetResult(InteractionResult.Failure<object?>(
            exception is UnauthorizedAccessException
                ? InteractionErrorCode.PERMISSION_DENIED
                : InteractionErrorCode.FAULT,
            exception.Message));
        Host.ReportInvocationFault(exception);
    }
}
