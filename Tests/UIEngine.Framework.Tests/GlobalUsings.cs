global using LanguageExt;
global using static LanguageExt.Prelude;

internal static class EitherTestExtensions
{
    public static R RightValue<L, R>(this Either<L, R> either) => either.Match(
        Right: static value => value,
        Left: static _ => throw new InvalidOperationException("Expected a Right value."));

    public static L LeftValue<L, R>(this Either<L, R> either) => either.Match(
        Right: static _ => throw new InvalidOperationException("Expected a Left value."),
        Left: static error => error);
}

internal static class OptionTestExtensions
{
    public static T SomeValue<T>(this Option<T> option) => option.Match(
        Some: static value => value,
        None: static () => throw new InvalidOperationException("Expected a Some value."));
}
