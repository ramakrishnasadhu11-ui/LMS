using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace LMS.Core.Repository.PagedList
{
    /// <summary>
    /// This class is used for Each entity properties get, get text ,get member names added.
    /// This extenstion is used in Unitofwork property level updates.
    /// </summary>
    public class LambdaExpressionHelper
    {
        private static readonly string expressionCannotBeNullMessage = "The expression cannot be null.";
        private static readonly string invalidExpressionMessage = "Invalid expression.";


        /// <summary>
        /// Gets the model name from a lambda expression.
        /// </summary>
        /// 
        /// <returns>
        /// The model name.
        /// </returns>
        /// <param name="expression">The expression.</param>
        public static string GetExpressionText(LambdaExpression expression)
        {
            Expression expressionBody = expression.Body;
            return GetMemberName(expressionBody);
        }
        /// <summary>
        /// Will return Membername in expression
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        private static string GetMemberName(Expression expression)
        {
            if (expression == null)
            {
                throw new ArgumentException(expressionCannotBeNullMessage);
            }

            if (expression is MemberExpression)
            {
                // Reference type property or field
                var memberExpression = (MemberExpression)expression;
                return memberExpression.Member.Name;
            }

            if (expression is MethodCallExpression)
            {
                // Reference type method
                var methodCallExpression = (MethodCallExpression)expression;
                return methodCallExpression.Method.Name;
            }

            if (expression is UnaryExpression)
            {
                // Property, field of method returning value type
                var unaryExpression = (UnaryExpression)expression;
                return GetMemberName(unaryExpression);
            }

            throw new ArgumentException(invalidExpressionMessage);
        }
        /// <summary>
        /// Will return member name in expression
        /// </summary>
        /// <param name="unaryExpression"></param>
        /// <returns></returns>
        private static string GetMemberName(UnaryExpression unaryExpression)
        {
            if (unaryExpression.Operand is MethodCallExpression)
            {
                var methodExpression = (MethodCallExpression)unaryExpression.Operand;
                return methodExpression.Method.Name;
            }

            return ((MemberExpression)unaryExpression.Operand).Member.Name;
        }
    }
}
