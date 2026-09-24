using System.ComponentModel.DataAnnotations;
using System.Reflection;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

internal sealed class LiveMethodNode : LiveReflectedMemberNode, IMethodNode
{
    private readonly Guid _Owner;
    private readonly MethodInfo _Method;
    private readonly List<IReadOnlyList<ValidationAttribute>> _ValidationAttributes;
    private readonly Lock _InvocationGate = new();
    private Task<Either<InteractionError, Option<object>>>? _ResultTask;

    public static Either<InteractionError, LiveMethodNode> Create(
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

        return Right(new LiveMethodNode(
            host, owner, member, method, parameters, resultType));
    }

    private static Either<InteractionError, LiveMethodNode> _Unsupported(MethodInfo method, string reason) =>
        Left(new InteractionError(
            InteractionErrorCode.UNSUPPORTED,
            $"Action '{method.Name}' is unsupported because {reason}."));

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

    public Task<Either<InteractionError, Option<object>>>? ResultTask
    {
        get
        {
            lock (_InvocationGate)
            {
                return _ResultTask;
            }
        }
    }

    public Either<InteractionError, InvocationStatus> Invoke(
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
            if (!target.IsRight)
            {
                return Left((InteractionError)target);
            }

            var unknown = arguments.Keys.FirstOrDefault(name =>
                Parameters.All(parameter => !StringComparer.Ordinal.Equals(parameter.Name, name)));
            if (unknown is not null)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Action '{Name}' has no parameter named '{unknown}'."));
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
                        return Left(new InteractionError(
                            InteractionErrorCode.INVALID_INPUT,
                            message,
                            [new ValidationIssue(
                                ValidationIssueCode.REQUIRED,
                                metadata.Name,
                                message)]));
                    }

                    bound[index] = metadata.HasDefaultValue
                        ? metadata.DefaultValue
                        : null;
                    continue;
                }

                var converted = ValueConversion.Convert(supplied, metadata.ParameterType);
                if (!converted.IsRight)
                {
                    return Left((InteractionError)converted);
                }

                var convertedValue = ((Option<object>)converted).IfNoneUnsafe((object?)null);
                var issues = ValueConversion.Validate(
                    convertedValue,
                    metadata.IsNullable,
                    metadata.Options,
                    metadata.Range,
                    _ValidationAttributes[index],
                    target.IfLeft(static error => throw new InvalidOperationException(error.Message)),
                    metadata.Name);
                if (issues.Count > 0)
                {
                    return Left(new InteractionError(
                        InteractionErrorCode.VALIDATION_FAILED,
                        $"Parameter '{metadata.Name}' failed validation.",
                        issues));
                }

                bound[index] = convertedValue;
            }

            TaskCompletionSource<Either<InteractionError, Option<object>>> completion;
            lock (_InvocationGate)
            {
                if (_ResultTask is { IsCompleted: false })
                {
                    return _AlreadyRunning();
                }

                completion = new TaskCompletionSource<Either<InteractionError, Option<object>>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _ResultTask = completion.Task;
            }

            try
            {
                var returned = _Method.Invoke(
                    target.IfLeft(static error => throw new InvalidOperationException(error.Message)),
                    bound);
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
                    completion.TrySetResult(Right(
                        Optional(returned)));
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

            return Right(_GetStatus(completion.Task));
        });

    private static InvocationStatus _GetStatus(Task<Either<InteractionError, Option<object>>> resultTask)
    {
        if (!resultTask.IsCompletedSuccessfully) return InvocationStatus.RUNNING;
        return resultTask.Result.IsRight
            ? InvocationStatus.SUCCEEDED
            : InvocationStatus.FAILED;
    }

    private async Task _CompleteAsync(
        TaskCompletionSource<Either<InteractionError, Option<object>>> completion,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            var result = ResultType is null
                ? null
                : task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
            completion.TrySetResult(Right(Optional(result)));
        }
        catch (Exception exception)
        {
            _CompleteFailure(completion, exception);
        }
    }

    private Either<InteractionError, InvocationStatus> _AlreadyRunning() =>
        Left(new InteractionError(
            InteractionErrorCode.UNAVAILABLE,
            $"Action '{Name}' is already running on this method node."));

    private void _CompleteFailure(
        TaskCompletionSource<Either<InteractionError, Option<object>>> completion,
        Exception exception)
    {
        completion.TrySetResult(Left(new InteractionError(
            exception is UnauthorizedAccessException
                ? InteractionErrorCode.PERMISSION_DENIED
                : InteractionErrorCode.FAULT,
            exception.Message)));
        Host.ReportInvocationFault(exception);
    }
}
