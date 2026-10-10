using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

internal static class PathResolution
{
    public static Either<InteractionError, ResolvedPath> Resolve(
        LogicalPath path)
    {
        var segments = path.Segments;
        if (segments.Count == 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The root list is not a navigable object."));
        }

        if (segments[0] is not RootLogicalPathSegment rootSegment)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The first path segment must identify a root by GUID."));
        }

        var root = UIEngineHost.Instance.ResolveRootNode(rootSegment.RootId);
        if (!root.IsRight)
        {
            return Left((InteractionError)root);
        }

        var resolvedRoot = (ResolvedNode)root;
        var canonical = resolvedRoot.CanonicalPath;
        BaseNode current = resolvedRoot.Node;
        var resolutionChain = new List<ResolvedNode>
        {
            new(canonical, current),
        };

        for (var index = 1; index < segments.Count; index++)
        {
            var segment = segments[index];
            Either<InteractionError, BaseNode> resolved = segment switch
            {
                MemberLogicalPathSegment member => _ResolveMember(current, member),
                ListLogicalPathSegment list => _ResolveListElement(current, list),
                DictLogicalPathSegment dictionary =>
                    _ResolveDictionaryValue(current, dictionary),
                RootLogicalPathSegment => Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    "A root path segment can only appear first.")),
                _ => Left(new InteractionError(
                    InteractionErrorCode.UNSUPPORTED,
                    "The logical path contains an unsupported segment.")),
            };
            if (!resolved.IsRight)
            {
                return Left((InteractionError)resolved);
            }

            canonical = canonical.Append(segment);
            current = (BaseNode)resolved;
            resolutionChain.Add(new ResolvedNode(canonical, current));
        }

        return Right(new ResolvedPath(resolutionChain));
    }

    private static Either<InteractionError, BaseNode> _ResolveMember(
        BaseNode current,
        MemberLogicalPathSegment segment)
    {
        if (current is not IObjectNode objectNode)
        {
            return _InvalidTraversal(segment.Name);
        }

        var matches = objectNode.Members
            .Where(member => StringComparer.Ordinal.Equals(member.Name, segment.Name))
            .ToArray();
        if (matches.Length == 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Member '{segment.Name}' was not found."));
        }

        if (matches.Length > 1)
        {
            return Left(new InteractionError(
                InteractionErrorCode.AMBIGUOUS,
                $"Member '{segment.Name}' is ambiguous."));
        }

        if (matches[0] is not LiveReferenceNode reference)
        {
            return Right(matches[0]);
        }

        var target = reference.ResolveTarget();
        return target.IsRight
            ? Right((BaseNode)(LiveResolvedReferenceNode)target)
            : Left((InteractionError)target);
    }

    private static Either<InteractionError, BaseNode> _ResolveListElement(
        BaseNode current,
        ListLogicalPathSegment segment)
    {
        if (current is not LiveCollectionNode collection)
        {
            return _InvalidTraversal($"index {segment.Index}");
        }

        return _ResolveSelectedObject(collection, collection.Select(segment));
    }

    private static Either<InteractionError, BaseNode> _ResolveDictionaryValue(
        BaseNode current,
        DictLogicalPathSegment segment)
    {
        if (current is not LiveCollectionNode collection)
        {
            return _InvalidTraversal($"key '{segment.Key}'");
        }

        return _ResolveSelectedObject(collection, collection.Select(segment));
    }

    private static Either<InteractionError, BaseNode> _ResolveSelectedObject(
        LiveCollectionNode collection,
        Either<InteractionError, IReadOnlyList<Guid>> selected)
    {
        if (!selected.IsRight)
        {
            return Left((InteractionError)selected);
        }

        var handles = selected.IfLeft(static _ => System.Array.Empty<Guid>());
        if (handles.Count == 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.NOT_FOUND,
                $"Selection on collection '{collection.Name}' matched no object."));
        }

        if (handles.Count > 1)
        {
            return Left(new InteractionError(
                InteractionErrorCode.AMBIGUOUS,
                $"Selection on collection '{collection.Name}' matched multiple objects."));
        }

        var resolved = UIEngineHost.Instance.CreateObjectNode(handles[0], collection.Name);
        return resolved.IsRight
            ? Right((BaseNode)(LiveObjectNode)resolved)
            : Left((InteractionError)resolved);
    }

    private static Either<InteractionError, BaseNode> _InvalidTraversal(string description) =>
        Left(new InteractionError(
            InteractionErrorCode.INVALID_INPUT,
            $"The {description} path segment cannot be applied at this location."));
}
