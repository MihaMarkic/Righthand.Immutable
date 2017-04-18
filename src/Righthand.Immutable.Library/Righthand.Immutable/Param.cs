namespace Righthand.Immutable
{
    public struct Param<T>
    {
        public T Value { get; set; }

        public static implicit operator Param<T>(T value)
        {
            return new Param<T> { Value = value };
        }

        public static implicit operator T(Param<T> param)
        {
            return param.Value;
        }
    }
}
