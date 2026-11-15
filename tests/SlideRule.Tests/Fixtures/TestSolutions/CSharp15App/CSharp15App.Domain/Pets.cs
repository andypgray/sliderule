namespace CSharp15App.Domain.Pets;

public record class Cat(string Name);

public record class Dog(string Name);

public union Pet(Cat, Dog);
