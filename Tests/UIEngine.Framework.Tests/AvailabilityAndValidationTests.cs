using System.ComponentModel.DataAnnotations;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Exposure;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies the distinct payload states and normalized step-6 validation contract.</summary>
public sealed class AvailabilityAndValidationTests
{
    [Fact]
    public async Task SuccessfulNullValuesAndEmptyReferencesRemainDistinctFromFailures()
    {
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var handle = host.RegisterRoot("model", new _ValidationModel()).Value;
        var descriptor = (await host.DescribeAsync(handle)).Value;
        var optionalText = Assert.Single(
            descriptor.Values,
            value => value.Id == nameof(_ValidationModel.OptionalText));
        var current = Assert.Single(descriptor.References);
        var throwing = Assert.Single(
            descriptor.Values,
            value => value.Id == nameof(_ValidationModel.Throwing));

        var nullValue = await optionalText.ReadAsync();
        var emptyReference = await current.ReadAsync();
        var emptyReferencePath = await host.Paths.ResolveAsync("/model/Current");
        var failedGetter = await throwing.ReadAsync();
        var missing = await host.Paths.ResolveAsync("/model/Unknown");
        host.Dispose();
        var unavailable = await optionalText.ReadAsync();

        Assert.True(nullValue.IsSuccess);
        Assert.Null(nullValue.Value);
        Assert.True(emptyReference.IsSuccess);
        Assert.Null(emptyReference.Value);
        Assert.Equal(BindingResolutionState.TEMPORARILY_UNAVAILABLE, emptyReferencePath.State);
        Assert.Equal(InteractionErrorCode.INVOCATION_FAILED, failedGetter.Error?.Code);
        Assert.Equal(BindingResolutionState.TARGET_MISSING, missing.State);
        Assert.Equal(InteractionErrorCode.TARGET_MISSING, missing.Error?.Code);
        Assert.Equal(InteractionErrorCode.HOST_DISPOSED, unavailable.Error?.Code);
    }

    [Fact]
    public async Task ReflectionMetadataIncludesNullabilityDefaultsSelectionsRangesAndRules()
    {
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var descriptor = (await host.DescribeAsync(
            host.RegisterRoot("model", new _ValidationModel()).Value)).Value;
        var count = Assert.Single(
            descriptor.Values,
            value => value.Id == nameof(_ValidationModel.Count));
        var mode = Assert.Single(
            descriptor.Values,
            value => value.Id == nameof(_ValidationModel.Mode));
        var apply = Assert.Single(descriptor.Actions, action => action.Id == nameof(_ValidationModel.Apply));
        var amount = Assert.Single(apply.Parameters, parameter => parameter.Id == "amount");
        var note = Assert.Single(apply.Parameters, parameter => parameter.Id == "note");

        Assert.False(count.IsNullable);
        Assert.Equal(1, count.Range?.Minimum);
        Assert.Equal(5, count.Range?.Maximum);
        Assert.Contains(count.ValidationRules, rule => rule.Kind == ValidationRuleKind.RANGE);
        Assert.Equal("widgets", count.Unit);
        Assert.Equal(["state", "bounded"], count.Tags);
        Assert.Equal([_Mode.OFF, _Mode.ON], mode.Options.Select(option => option.Value));
        Assert.Contains(mode.ValidationRules, rule => rule.Kind == ValidationRuleKind.FINITE_SELECTION);
        Assert.Null(mode.Range);
        Assert.True(amount.IsRequired);
        Assert.False(amount.HasDefaultValue);
        Assert.False(amount.IsNullable);
        Assert.False(note.IsRequired);
        Assert.True(note.HasDefaultValue);
        Assert.Null(note.DefaultValue);
        Assert.True(note.IsNullable);
        Assert.Empty(note.Tags);
        Assert.Equal(ActionRisk.DESTRUCTIVE, apply.Risk);
        Assert.True(apply.RequiresConfirmation);
    }

    [Fact]
    public async Task ConversionPrecedesValidationAndRejectedInputsDoNotExecuteDomainCode()
    {
        var model = new _ValidationModel();
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;
        var count = Assert.Single(
            descriptor.Values,
            value => value.Id == nameof(_ValidationModel.Count));
        var apply = Assert.Single(descriptor.Actions, action => action.Id == nameof(_ValidationModel.Apply));

        var badConversion = await count.WriteAsync("not-a-number");
        var badRange = await count.WriteAsync("9");
        var badArgument = await apply.InvokeAsync(new Dictionary<string, object?> { ["amount"] = "9" });
        var valid = await apply.InvokeAsync(new Dictionary<string, object?> { ["amount"] = "3" });
        var validCompletion = await valid.Value.Completion;

        Assert.Equal(InteractionIssueCode.CONVERSION_FAILED, Assert.Single(badConversion.Error!.Issues).Code);
        Assert.Equal(InteractionIssueCode.OUT_OF_RANGE, Assert.Single(badRange.Error!.Issues).Code);
        Assert.Equal(InteractionIssueTarget.VALUE, Assert.Single(badRange.Error.Issues).Target);
        Assert.Equal("Count", Assert.Single(badRange.Error.Issues).TargetId);
        Assert.Equal(InteractionIssueTarget.PARAMETER, Assert.Single(badArgument.Error!.Issues).Target);
        Assert.Equal(0, model.SetterInvocationCount);
        Assert.Equal(1, model.ActionInvocationCount);
        Assert.Equal(3, validCompletion.Value);
    }

    [Fact]
    public async Task ProgrammaticMetadataAndValidationUseTheSameDescriptorContract()
    {
        var registry = new ExposureRegistry();
        registry.For<_ThirdPartyModel>()
            .Value("score", static model => model.Score, static (model, value) => model.Score = value)
            .Range("score", 1, 6)
            .Selection("score", 2, 4, 6)
            .Validate<int>("score", static (_, value) => value == 4 ? "Four is reserved." : null)
            .ValidateAsync<int>("score", static (_, value, _) => ValueTask.FromResult(
                value == 2 ? "Two is asynchronously reserved." : null))
            .Metadata("score", "points", "ranked");
        using var host = new UIEngineHost(new UIEngineHostOptions { Exposure = registry });
        var model = new _ThirdPartyModel();
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;
        var score = Assert.Single(descriptor.Values);

        var notSelected = await score.WriteAsync("3");
        var domainRejected = await score.WriteAsync("4");
        var asyncRejected = await score.WriteAsync("2");
        var accepted = await score.WriteAsync("6");

        Assert.Equal(4, score.ValidationRules.Count);
        Assert.Equal("points", score.Unit);
        Assert.Equal(["ranked"], score.Tags);
        Assert.Equal(InteractionIssueCode.NOT_IN_SELECTION, Assert.Single(notSelected.Error!.Issues).Code);
        Assert.Equal(InteractionIssueCode.RULE_FAILED, Assert.Single(domainRejected.Error!.Issues).Code);
        Assert.Equal(InteractionIssueCode.RULE_FAILED, Assert.Single(asyncRejected.Error!.Issues).Code);
        Assert.Equal(6, accepted.Value);
        Assert.Equal(6, model.Score);
    }

    [Fact]
    public async Task PermissionLikeActionRejectionHasAStableCodeAndContext()
    {
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var descriptor = (await host.DescribeAsync(
            host.RegisterRoot("model", new _ValidationModel()).Value)).Value;
        var denied = Assert.Single(descriptor.Actions, action => action.Id == nameof(_ValidationModel.Denied));

        var result = await denied.InvokeAsync(new Dictionary<string, object?>());
        var completion = await result.Value.Completion;

        Assert.Equal(InteractionErrorCode.PERMISSION_DENIED, completion.Error?.Code);
        var issue = Assert.Single(completion.Error!.Issues);
        Assert.Equal(InteractionIssueCode.PERMISSION_DENIED, issue.Code);
        Assert.Equal(InteractionIssueTarget.PERMISSION, issue.Target);
        Assert.Equal(nameof(_ValidationModel.Denied), issue.TargetId);
    }

    [Fact]
    public async Task ActionPreconditionRejectsACompleteValidArgumentSetBeforeInvocation()
    {
        var model = new _ValidationModel { AllowApply = false };
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;
        var apply = Assert.Single(descriptor.Actions, action => action.Id == nameof(_ValidationModel.Apply));

        var result = await apply.InvokeAsync(new Dictionary<string, object?> { ["amount"] = "3" });

        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, result.Error?.Code);
        var issue = Assert.Single(result.Error!.Issues);
        Assert.Equal(InteractionIssueCode.ACTION_REJECTED, issue.Code);
        Assert.Equal(InteractionIssueTarget.ACTION, issue.Target);
        Assert.Equal(0, model.ActionInvocationCount);
    }

    private sealed class _ValidationModel
    {
        private int _Count;

        public int SetterInvocationCount { get; private set; }

        public int ActionInvocationCount { get; private set; }

        public bool AllowApply { get; set; } = true;

        [Children]
        public _Child? Current { get; set; }

        [Expose]
        [Range(1, 5)]
        [InteractionMetadata(Unit = "widgets", Tags = ["state", "bounded"])]
        public int Count
        {
            get => _Count;
            set
            {
                SetterInvocationCount++;
                _Count = value;
            }
        }

        [Expose]
        public _Mode Mode { get; set; }

        [Expose]
        public string? OptionalText { get; set; }

        [Expose]
        public string Throwing
        {
            get
            {
                _ = _Count;
                throw new InvalidOperationException("Getter failed.");
            }
        }

        [Action(
            Precondition = nameof(CanApply),
            Risk = ActionRisk.DESTRUCTIVE,
            RequiresConfirmation = true)]
        public int Apply([Range(1, 5)] int amount, string? note = null)
        {
            ActionInvocationCount++;
            return amount;
        }

        private bool CanApply() => AllowApply;

        [Action]
        public void Denied()
        {
            ActionInvocationCount++;
            throw new UnauthorizedAccessException("The action is not permitted.");
        }
    }

    private sealed class _Child;

    private sealed class _ThirdPartyModel
    {
        public int Score { get; set; }
    }

    private enum _Mode
    {
        OFF,
        ON,
    }
}
