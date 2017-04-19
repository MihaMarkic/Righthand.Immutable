namespace Righthand.Immutable
{
    public static class Common
    {
        public static string PascalCasing(string name)
        {
            if (name == null)
            {
                return null;
            }
            return char.ToUpper(name[0]) + name.Substring(1);
        }
    }
}
