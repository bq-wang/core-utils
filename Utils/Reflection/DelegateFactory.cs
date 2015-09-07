using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Utils.Reflection
{
    public static class DelegateFactory
    {
        #region Fields

        private const string DisplayClassMethodName = "__invoke";
        private const string DelegateInvokeMethodName = "Invoke";

        private readonly static ConcurrentDictionary<Type, Func<Func<object[], object>, Delegate>> DelegateCreatorCache
            = new ConcurrentDictionary<Type, Func<Func<object[], object>, Delegate>>();

        #endregion

        private static Type BuildDisplayClass(Type delegateType)
        {
            var delMethodInfo = delegateType.GetMethod(DelegateInvokeMethodName);
            var paramTypes = delMethodInfo.GetParameters().Select(x => x.ParameterType).ToArray();
            var invisibleParamTypes = paramTypes.Where(x => !x.IsVisible).ToArray();
            if (invisibleParamTypes.Length > 0)
            {
                var tmp = "'" + string.Join("', '", invisibleParamTypes.Select(x => x.FullName)) + "'";
                var msg = string.Format("Exception occurred while building delegate creator for: '{0}'. The following types need to be public and visible to external assemblies {1}.",
                        delegateType.FullName, tmp);
                throw new Exception(msg);
            }

            var baseType = typeof(object);
            var builder = TypeHelper.BuildClass(baseType);

            var fieldBuilder = builder.DefineField("Func", typeof(Func<object[], object>), FieldAttributes.Public);

            var baseCtor = baseType.GetConstructor(Type.EmptyTypes);
            var ctorBuilder = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
                                                        new[] { typeof(Func<object[], object>) });
            var generator = ctorBuilder.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, baseCtor);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Stfld, fieldBuilder);
            generator.Emit(OpCodes.Ret);

            var methodBuilder = builder.DefineMethod(DisplayClassMethodName, MethodAttributes.Public, delMethodInfo.ReturnType, paramTypes);
            var methodGenerator = methodBuilder.GetILGenerator();
            methodGenerator.DeclareLocal(typeof(object[]));
            methodGenerator.DeclareLocal(typeof(string));
            methodGenerator.Emit(OpCodes.Ldc_I4, paramTypes.Length);
            methodGenerator.Emit(OpCodes.Newarr, typeof(object));
            methodGenerator.Emit(OpCodes.Stloc_0);
            for (int i = 0; i < paramTypes.Length; i++)
            {
                methodGenerator.Emit(OpCodes.Ldloc_0);
                methodGenerator.Emit(OpCodes.Ldc_I4, i);
                methodGenerator.Emit(OpCodes.Ldarg, i + 1);
                if (paramTypes[i].IsValueType)
                    methodGenerator.Emit(OpCodes.Box, paramTypes[i]);
                methodGenerator.Emit(OpCodes.Stelem_Ref);
            }

            var mi2 = typeof(Func<object[], object>).GetMethod(DelegateInvokeMethodName);
            methodGenerator.Emit(OpCodes.Ldarg_0);
            methodGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
            methodGenerator.Emit(OpCodes.Ldloc_0);
            methodGenerator.Emit(OpCodes.Callvirt, mi2);

            if (delMethodInfo.ReturnType == typeof(void))
            {
                methodGenerator.Emit(OpCodes.Pop);
            }
            else
            {
                if (delMethodInfo.ReturnType.IsValueType)
                    methodGenerator.Emit(OpCodes.Unbox_Any, delMethodInfo.ReturnType);
                else
                    methodGenerator.Emit(OpCodes.Castclass, delMethodInfo.ReturnType);
                var label = methodGenerator.DefineLabel();
                methodGenerator.Emit(OpCodes.Stloc_1);
                methodGenerator.Emit(OpCodes.Br_S, label);
                methodGenerator.MarkLabel(label);
                methodGenerator.Emit(OpCodes.Ldloc_1);
            }
            methodGenerator.Emit(OpCodes.Ret);
            return builder.CreateType();
        }

        /// <summary>
        /// Get a function to create delegate. The function is 2.5 times slower than direct newing a delegate.
        /// Thread safe.
        /// </summary>
        /// <returns></returns>
        public static Func<Func<object[], object>, Delegate> GetDelegateCreator(Type delegateType)
        {
            Func<Func<object[], object>, Delegate> res;
            if (DelegateCreatorCache.TryGetValue(delegateType, out res))
                return res;

            var delegateCtorInfo = delegateType.GetConstructors()[0];
            var displayClassType = BuildDisplayClass(delegateType);
            var displayClassCtorInfo = displayClassType.GetConstructor(new Type[] { typeof(Func<object[], object>) });
            var mi = displayClassType.GetMethod(DisplayClassMethodName);

            var dynamicMethod = new DynamicMethod("gen__GetDelegateCreator",
                                    typeof(Delegate), new[] { typeof(Func<object[], object>) }, true);
            var il = dynamicMethod.GetILGenerator();
            il.DeclareLocal(displayClassType);
            il.DeclareLocal(delegateType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, displayClassCtorInfo);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldftn, mi);
            il.Emit(OpCodes.Newobj, delegateCtorInfo);
            il.Emit(OpCodes.Stloc_1);

            var label = il.DefineLabel();
            il.Emit(OpCodes.Br_S, label);
            il.MarkLabel(label);
            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Ret);

            var del = dynamicMethod.CreateDelegate(typeof(Func<Func<object[], object>, Delegate>));
            res = (Func<Func<object[], object>, Delegate>)del;
            DelegateCreatorCache[delegateType] = res;
            return res;
        }

        /// <summary>
        /// Creates a delegate fastly. 7 times slower than direct newing a delegate.
        /// Thread safe.
        /// </summary>
        public static Delegate FastCreate(Type delegateType, Func<object[], object> dynamicInvoker)
        {
            return GetDelegateCreator(delegateType)(dynamicInvoker);
        }

        /// <summary>
        /// Creates a delegate fastly. 7 times slower than direct newing a delegate.
        /// Thread safe.
        /// </summary>
        public static T FastCreate<T>(Func<object[], object> dynamicInvoker) where T : class 
        {
            return GetDelegateCreator(typeof(T))(dynamicInvoker) as T;
        }
    }
}
