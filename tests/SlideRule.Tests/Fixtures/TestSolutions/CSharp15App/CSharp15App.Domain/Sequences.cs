using System.Collections.Generic;
using System.Linq;

namespace CSharp15App.Domain.Sequences;

public static class SequenceIndexer
{
    extension(IEnumerable<int> sequence)
    {
        public int this[int index] => sequence.ElementAt(index);
    }
}
