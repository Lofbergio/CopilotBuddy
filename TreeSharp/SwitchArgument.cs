using System;

namespace TreeSharp
{
    public class SwitchArgument<T>
    {
        public Composite Branch { get; set; }
        public T RequiredValue { get; set; }

        public SwitchArgument(Composite branch, T requiredValue)
        {
            Branch = branch;
            RequiredValue = requiredValue;
        }

        public SwitchArgument(T requiredValue, Composite branch) : this(branch, requiredValue) { }
    }
}
