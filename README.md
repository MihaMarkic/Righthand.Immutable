# Righthand.Immutable

[![NuGet](https://img.shields.io/nuget/v/Righthand.Immutable.svg)](https://www.nuget.org/packages/Righthand.Immutable)

An open source tool that provides support for generating C# immutable types and a spread operator over generated immutable types. It allows easier creation of immutable types by generating required code out of a constructor.

Current features:

* Place caret on type declaration and pick Implement immutable type refactoring. It will create or replace all required properties with readonly properties and create or replace Clone method. 
* Creates getter properties and a spread-like operator though Clone method

It works together with [Righthand.Immutable](https://www.nuget.org/packages/Righthand.Immutable) NuGet package.

## Visual Studio Extension Release notes
1.1.2
- derived types with empty constructors can be made immutable

1.1.1
- supports abstract types and properties
- no Clone method for abstract types

1.1.0
- support for inheritance
    - base type has to have a single constructor
    - missing arguments (ones from base type constructor) in constructor are added if not present
    - call to base(...) initializer is automatically added when missing

1.0.3
- properties with getter bodies and no setters are persisted
- lambda properties are persisted
- all methods not named Clone are persisted

1.0.0
- first version

## Sample immutable type creation

1. Install [Righthand.Immutable](https://marketplace.visualstudio.com/items?itemName=MihaMarkic.RighthandImmutable) VSIX extension
1. Create a new .net project.  
2. Add support code (basically a single struct)  
a) Add [Righthand.Immutable](https://www.nuget.org/packages/Righthand.Immutable) NuGet package or  
b) include this code somewhere in the project
```csharp
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
```
3. Add a "to-be" immutable class with a constructor containg arguments that are mapped to properties
```csharp
public class MyImmutable
{
    public MyImmutable(int number, string text)
    { }
}
```
4. Place a caret over MyImmutable class name
5. Pick "Implement immutable type" refactoring.
6. Final code should look like:
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
