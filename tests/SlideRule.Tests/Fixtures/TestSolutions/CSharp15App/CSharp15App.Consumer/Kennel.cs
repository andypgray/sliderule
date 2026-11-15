using System.Collections.Generic;
using CSharp15App.Domain.Gates;
using CSharp15App.Domain.Pets;
using CSharp15App.Domain.Sequences;

namespace CSharp15App.Consumer;

public sealed class Kennel
{
    public string NameOf(Pet pet) => pet switch
    {
        Dog dog => dog.Name,
        Cat cat => cat.Name,
    };

    public Pet Adopt() => new Dog("Rex");

    public string Gate(GateState state) => Describer.Describe(state);

    public int Third(IEnumerable<int> values) => values[2];

    public List<string> Copy(string[] values) => [with(capacity: values.Length * 2), .. values];
}
