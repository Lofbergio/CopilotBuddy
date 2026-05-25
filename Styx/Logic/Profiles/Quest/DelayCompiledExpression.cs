using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Styx.Logic.Profiles.Quest
{
    /// <summary>
    /// Holds a C# expression string whose delegate is compiled lazily by CompileBatch.
    /// The CallableExpression property is always callable but throws if Compile() has not been called.
    /// Ported from HB 6.2.3 Styx.CommonBot.Profiles.Quest.Order.DelayCompiledExpression.
    /// </summary>
    public class DelayCompiledExpression
    {
        internal Delegate CompiledExpression;

        public DelayCompiledExpression(string expression, Type delegateType)
        {
            ExpressionString = expression
                .Replace(Environment.NewLine, "\n")
                .Replace("\n", Environment.NewLine)
                .Trim();
            DelegateType = delegateType;

            // Build a CallableExpression that checks CompiledExpression at invoke time.
            FieldInfo field = GetType().GetField("CompiledExpression", BindingFlags.Instance | BindingFlags.NonPublic);
            Expression compiledField = Expression.Field(Expression.Constant(this), field);
            var notCompiledEx = new InvalidOperationException(
                "The expression set that owns this expression has not yet been compiled");
            ParameterExpression[] parameters = delegateType
                .GetMethod("Invoke")
                .GetParameters()
                .Select(p => Expression.Parameter(p.ParameterType))
                .ToArray();

            CallableExpression = Expression.Lambda(
                delegateType,
                Expression.Block(
                    Expression.IfThen(
                        Expression.Equal(compiledField, Expression.Constant(null)),
                        Expression.Throw(Expression.Constant(notCompiledEx))),
                    Expression.Call(
                        Expression.Convert(compiledField, delegateType),
                        delegateType.GetMethod("Invoke"),
                        parameters)),
                parameters).Compile();
        }

        public Delegate CallableExpression { get; }
        public Type DelegateType { get; private set; }
        public string ExpressionString { get; private set; }

        /// <summary>True if the owning CompileBatch compiled successfully and this expression is callable.</summary>
        public bool IsCompiled => CompiledExpression != null;

        public static DelayCompiledExpression<Func<bool>> Condition(string conditionString)
        {
            if (string.IsNullOrWhiteSpace(conditionString))
                throw new ArgumentException("Condition is null or white space", "conditionString");

            if (bool.TryParse(conditionString, out bool flag))
                conditionString = flag ? "true" : "false";

            return new DelayCompiledExpression<Func<bool>>("() => " + conditionString);
        }
    }

    /// <summary>
    /// Typed variant of DelayCompiledExpression.
    /// Ported from HB 6.2.3 Styx.CommonBot.Profiles.Quest.Order.DelayCompiledExpression&lt;T&gt;.
    /// </summary>
    public class DelayCompiledExpression<T> : DelayCompiledExpression
    {
        public DelayCompiledExpression(string expression)
            : base(expression, typeof(T))
        {
        }

        public new T CallableExpression => (T)(object)base.CallableExpression;
    }
}
