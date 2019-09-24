﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

#if NEMESIS_BINARY_PACKAGE
namespace Nemesis.Essentials.Runtime
#else
namespace $rootnamespace$.Runtime
#endif
{
#if NEMESIS_BINARY_PACKAGE
    public
#else
    internal
#endif
    static partial class TypeMeta
    {
        #region Nullable
        /// <summary>
        /// Checks whether the given <paramref name="type"/> cref="type"/> is <see cref="Nullable{T}"/>.
        /// </summary>
        public static bool IsNullable(this Type type) => type.IsValueType && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

        /// <summary>
        /// Gets the underlying <see cref="ValueType"/> from type of <see cref="Nullable{T}"/>.
        /// </summary>
        public static Type GetNullableUnderlyingType(this Type type)
        {
            if(!type.IsNullable()) throw new ArgumentException("Type should be nullable");
            return type.GetGenericArguments().Single();
        }

        public static Type GetNullableType(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return type.IsValueType && !IsNullable(type) ? typeof(Nullable<>).MakeGenericType(type) : type;
        }
        #endregion

        #region Generics

        public static Type GetConcreteInterfaceOfType(Type type, Type generic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (generic == null) throw new ArgumentNullException(nameof(generic));
            if (!generic.IsGenericTypeDefinition) throw new ArgumentException($@"{nameof(generic)} has to be GenericTypeDefinition", nameof(generic));

            foreach (Type @interface in type.GetInterfaces())
                if (@interface.IsGenericType && !@interface.IsGenericTypeDefinition && @interface.GetGenericTypeDefinition() == generic)
                    return @interface;

            return null;
        }

        public static IEnumerable<Type> GetAllInterfaces(Type type)
        {
            foreach (var interfaceType in type.GetInterfaces())
            {
                yield return interfaceType;

                foreach (var i in GetAllInterfaces(interfaceType))
                    yield return i;
            }
        }

        public static bool DerivesOrImplementsGeneric(this Type type, Type generic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (generic == null) throw new ArgumentNullException(nameof(generic));

            return generic.IsInterface ? type.ImplementsGenericInterface(generic) : type.DerivesFromGenericClass(generic);
        }

        /// <summary>
        /// Determines whether given type derives from given generic type or not
        /// </summary>
        /// <param name="type">Type that is being tested for derivation</param>
        /// <param name="generic">Generic base type</param>
        private static bool DerivesFromGenericClass(this Type type, Type generic)
        {
            if (type == generic || generic.IsAssignableFrom(type) || type.IsSubclassOf(generic)) return true;
            if (!generic.IsGenericType) return false;

            if (generic.IsGenericTypeDefinition)
            {
                while (type != null)
                {
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == generic) return true;
                    type = type.BaseType;
                }
                return false;
            }
            else
            {
                while (type != null)
                {
                    if (type == generic) return true;
                    type = type.BaseType;
                }
                return false;
            }
        }

        /// <summary>
        /// Determines whether given type implements given generic interface or not
        /// </summary>
        /// <param name="type">Type that is being tested for interface implementation</param>
        /// <param name="generic">Generic base type</param>
        /// <returns>true if type implements given generic interface, false otherwise</returns>
        private static bool ImplementsGenericInterface(this Type type, Type generic) =>
            type == generic ||
            type.IsGenericType && type.GetGenericTypeDefinition() == generic ||
            type.GetInterfaces().Any(t => t.IsGenericType && t.ImplementsGenericInterface(generic));

        public static IEnumerable<Type> GetGenericInterfaces(this Type type, Type generic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (generic == null) throw new ArgumentNullException(nameof(generic));

            return type.GetInterfaces().Where(t => t.ImplementsGenericInterface(generic));
        }

        /* TODO: try this one day
             public static bool ImplementsGenericInterface(this Type type, Type genericInterface)
            {
                Guard.AgainstViolation(genericInterface.IsGenericTypeDefinition && genericInterface.IsInterface,
                    "Only generic interface can be used");

                return type.GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericTypeDefinition())
                    .Contains(genericInterface);
            }*/

        public static bool IsCovariant(Type t) => 0 != (t.GenericParameterAttributes & GenericParameterAttributes.Covariant);

        public static bool IsContravariant(Type t) => 0 != (t.GenericParameterAttributes & GenericParameterAttributes.Contravariant);

        public static bool IsInvariant(Type t) => 0 == (t.GenericParameterAttributes & GenericParameterAttributes.VarianceMask);

        #endregion

        /// <summary>
        /// Checks whether <paramref name="memberOrType"/> has specific attribute <typeparamref name="TAttribute"/>.
        /// </summary>
        public static bool HasAttribute<TAttribute>(this MemberInfo memberOrType) where TAttribute : Attribute => memberOrType.GetCustomAttributes<TAttribute>(true).Any();

        public static bool IsPublic(this MemberInfo mi) =>
            mi switch
            {
                null => false,
                FieldInfo fi => fi.IsPublic,
                PropertyInfo pi => (pi.GetMethod?.IsPublic ?? false) || (pi.SetMethod?.IsPublic ?? false),
                EventInfo ei => (ei.AddMethod?.IsPublic ?? false) || (ei.RemoveMethod?.IsPublic ?? false),
                MethodBase mb => mb.IsPublic,
                _ => false,
            };

        /// <summary>
        /// Checks whether <paramref name="member"/> is generated by compiler.
        /// </summary>
        public static bool IsCompilerGenerated(this MemberInfo member) => member.HasAttribute<CompilerGeneratedAttribute>();

        #region Property

        /// <summary>
        /// Checks whether <paramref name="property"/> is auto-implemented property.
        /// </summary>
        public static bool IsAutoProperty(this PropertyInfo property)
        {
            MethodInfo setMethod = property.GetSetMethod(true);
            MethodInfo getMethod = property.GetGetMethod(true);
            return setMethod != null && setMethod.IsCompilerGenerated() &&
                   getMethod != null && getMethod.IsCompilerGenerated();
        }

        private static readonly Regex _backingFieldRegex = new Regex(@"^\<(?<propertyName>\w+)\>k__BackingField$", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Returns <see cref="FieldInfo"/> of backing field for given auto-property.
        /// </summary>
        public static FieldInfo GetBackingField(this PropertyInfo property)
        {
            FieldInfo result = (property.DeclaringType ?? throw new ArgumentNullException(nameof(property), $@"{nameof(property)}.DeclaringType is null"))
                .GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (result == null) throw new ArgumentException($"No backing field found for property {property.Name} in {property.DeclaringType.FullName}");

            return result;
        }

        /// <summary>
        /// Checks whether <paramref name="field"/> is a backing field for some auto-implemented property.
        /// </summary>
        public static bool IsBackingField(this FieldInfo field) => field.IsCompilerGenerated() && _backingFieldRegex.IsMatch(field.Name);

        /// <summary>
        /// Tries to find auto-property that causes generation of <paramref name="field"/>, or returns <c>false</c>
        /// if <paramref name="field"/> is not auto-property backing field.
        /// </summary>
        public static bool TryGetDeclaringProperty(this FieldInfo field, out PropertyInfo declaringProperty)
        {
            const BindingFlags DECLARED_INSTANCE_MEMBER = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (!field.IsCompilerGenerated())
            {
                declaringProperty = null;
                return false;
            }
            Match match = _backingFieldRegex.Match(field.Name);
            if (!match.Success)
            {
                declaringProperty = null;
                return false;
            }

            declaringProperty = (field.DeclaringType ?? throw new ArgumentNullException(nameof(field), $@"{nameof(field)}.DeclaringType is null"))
                .GetProperty(match.Groups["propertyName"].Value, DECLARED_INSTANCE_MEMBER);
            return declaringProperty != null;
        }

        /// <summary>
        /// Tries to find auto-event that causes generation of <paramref name="field"/>, or returns <c>false</c>
        /// if <paramref name="field"/> is not auto-event backing field.
        /// </summary>
        public static bool TryGetDeclaringEvent(this FieldInfo field, out EventInfo declaringEvent)
        {
            const BindingFlags DECLARED_INSTANCE_MEMBER = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (!field.IsPrivate)
            {
                declaringEvent = null;
                return false;
            }
            declaringEvent = (field.DeclaringType ?? throw new ArgumentNullException(nameof(field), $@"{nameof(field)}.DeclaringType is null"))
                .GetEvent(field.Name, DECLARED_INSTANCE_MEMBER);
            return declaringEvent != null;
        }

        public static PropertyInfo GetIndexer(Type type, BindingFlags bindingFlags, params Type[] arguments) =>
            type.GetProperties(bindingFlags).Single(x => x.GetIndexParameters().Select(y => y.ParameterType).SequenceEqual(arguments));

        #endregion

        #region Lazy

        public static bool IsLazyType(Type type) =>
            (
                type.ImplementsGenericInterface(typeof(Lazy<>)) ||
                type.ImplementsGenericInterface(typeof(IQueryable<>)) || typeof(IQueryable).IsAssignableFrom(type) ||
                type.ImplementsGenericInterface(typeof(IEnumerable<>)) || typeof(IEnumerable).IsAssignableFrom(type) ||
                type.ImplementsGenericInterface(typeof(IEnumerator<>)) || typeof(IEnumerator).IsAssignableFrom(type)
            )
            &&
            !IsCollection(type);

        private static bool IsCollection(Type type) =>
            type.ImplementsGenericInterface(typeof(ICollection<>)) ||
            typeof(ICollection).IsAssignableFrom(type) ||
            type.ImplementsGenericInterface(typeof(IReadOnlyCollection<>)) ||
            type.Name.EndsWith("Collection", StringComparison.InvariantCulture) ||
            type.Name.EndsWith("List", StringComparison.InvariantCulture);
        #endregion

        #region Number

        public static bool IsNumeric(Type type)
            => type == typeof(double) || type == typeof(float) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(decimal);

        /* //TODO: measure performance with the following
         public static bool IsNumeric(Type type)
                {
                    type = GetNullableUnderlyingType(type);
                    if (!type.IsEnum)
                    {
                        switch (Type.GetTypeCode(type))
                        {
                            //case TypeCode.Char:
                            case TypeCode.SByte:
                            case TypeCode.Byte:
                            case TypeCode.Int16:
                            case TypeCode.Int32:
                            case TypeCode.Int64:
                            case TypeCode.Double:
                            case TypeCode.Single:
                            case TypeCode.UInt16:
                            case TypeCode.UInt32:
                            case TypeCode.UInt64:
                            case TypeCode.Decimal:
                                return true;
                        }
                    }
                    return false;
                }*/

        public static bool IsNumeric(object obj)
            => obj is byte || obj is sbyte ||
               obj is short || obj is ushort ||
               obj is int || obj is uint ||
               obj is long || obj is ulong ||
               obj is double || obj is float ||
               obj is decimal;

        #endregion
    }
}
