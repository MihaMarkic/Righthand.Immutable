# Righthand.Immutable

[![NuGet](https://img.shields.io/nuget/v/Righthand.Immutable.svg)](https://www.nuget.org/packages/Righthand.Immutable)

This library is a part of an open source tool that provides support for generating C# immutable types and a spread operator over generated immutable types. It allows easier creation of immutable types by generating required code out of a constructor.

Current features:

    Place caret on type declaration and pick Implement immutable type refactoring. It will create or replace all required properties with readonly properties and create or replace Clone method. 

It works together with [Righthand.Immutable](https://www.nuget.org/packages/Righthand.Immutable) NuGet package.

## Sample immutable type creation

1. Install [Righthand.Immutable](https://marketplace.visualstudio.com/items?itemName=MihaMarkic.RighthandImmutable) VSIX extension
1. Create a new .net project.  
1. Add [Righthand.Immutable](https://www.nuget.org/packages/Righthand.Immutable) NuGet package
1. Add a "to-be" immutable class with a constructor containg arguments that are mapped to properties
```csharp
public class MyImmutable
{
    public MyImmutable(int number, string text)
    { }
}
```
5. Place a caret over MyImmutable class name
6. Pick "Implement immutable type" refactoring.
7. Final code should look like:
```csharp
public class MyImmutable
    {
        public int Number { get; }
        public string Text { get; }

        public MyImmutable(int number, string text)
        {
            Number = number;
            Text = text;
        }

        public MyImmutable Clone(Param<int>? number = null, Param<string>? text = null)
        {
            return new MyImmutable(number.HasValue ? number.Value.Value : Number,
text.HasValue ? text.Value.Value : Text);
        }
    }
```

### Use MyImmutable like:
```csharp
MyImmutable original = new MyImmutable(5, "Five");
MyImmutable copy = original.Clone(text: "Four");  // Clone method is basically a spread operator over MyImmutable
```
Where copy is a new instance of original with property Text replaced.
