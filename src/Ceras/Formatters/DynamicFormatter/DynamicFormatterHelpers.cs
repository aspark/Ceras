﻿namespace Ceras.Formatters
{
	using Exceptions;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Linq.Expressions;
	using System.Reflection;
	using Helpers;

	static class DynamicFormatterHelpers
	{
		static readonly MethodInfo _setValue = typeof(FieldInfo).GetMethod(
																		   name: "SetValue",
																		   bindingAttr: BindingFlags.Instance | BindingFlags.Public,
																		   binder: null,
																		   types: new Type[] { typeof(object), typeof(object) },
																		   modifiers: new ParameterModifier[2]);

		// A very simple implementation of a dynamic serializer.
		// Uses reflection, so its very slow. Only used as a fallback for AOT compiled scenarios,
		// and only while the source code generator is not done yet.
		/*
		internal static SerializeDelegate<T> CreateSimpleSerializer<T>(CerasSerializer ceras, Schema schema)
		{
			object[] args = new object[3];
			return (ref byte[] buffer, ref int offset, T value) =>
			{
				for (var memberIndex = 0; memberIndex < schema.Members.Count; memberIndex++)
				{
					var member = schema.Members[memberIndex];

					var formatter = ceras.GetReferenceFormatter(member.MemberType);
					var method = formatter.GetType().GetMethod("Serialize");

					args[0] = buffer;
					args[1] = offset;
					args[2] = new SerializedMember()


				}
			};
		}
		*/


		// A member has been deserialized into a local variable, and now it has to be written back to its actual field (which is readonly)
		// Depending on the setting we'll do different things here.
		internal static void EmitReadonlyWriteBack(Type type, ReadonlyFieldHandling readonlyFieldHandling, FieldInfo fieldInfo, ParameterExpression refValueArg, ParameterExpression tempStore, List<Expression> block)
		{
			if (readonlyFieldHandling == ReadonlyFieldHandling.ExcludeFromSerialization)
				throw new InvalidOperationException($"Error while trying to generate a deserializer for the field '{fieldInfo.DeclaringType.FullName}.{fieldInfo.Name}': the field is readonly, but ReadonlyFieldHandling is turned off in the configuration.");

			// 4. ReferenceTypes and ValueTypes are handled a bit differently (Boxing, Equal-vs-ReferenceEqual, text in exception, ...)
			if (type.IsValueType)
			{
				// Value types are simple.
				// Either they match perfectly -> do nothing
				// Or the values are not the same -> either throw an exception of do a forced overwrite

				Expression onMismatch;
				if (readonlyFieldHandling == ReadonlyFieldHandling.ForcedOverwrite)
					// field.SetValue(valueArg, tempStore)
					onMismatch = Expression.Call(Expression.Constant(fieldInfo), _setValue, arg0: refValueArg, arg1: Expression.Convert(tempStore, typeof(object))); // Explicit boxing needed
				else
					onMismatch = Expression.Throw(Expression.Constant(new CerasException($"The value-type in field '{fieldInfo.Name}' does not match the expected value, but the field is readonly and overwriting is not allowed in the configuration. Make the field writeable or enable 'ForcedOverwrite' in the serializer settings to allow Ceras to overwrite the readonly-field.")));

				block.Add(Expression.IfThenElse(
									 test: Expression.Equal(tempStore, Expression.MakeMemberAccess(refValueArg, fieldInfo)),
									 ifTrue: Expression.Empty(),
									 ifFalse: onMismatch
									));
			}
			else
			{
				// Either we already give the deserializer the existing object as target where it should write to, in which case its fine.
				// Or the deserializer somehow gets its own object instance from somewhere else, in which case we can only proceed with overwriting the field anyway.

				// So the most elegant way to handle this is to first let the deserializer do what it normally does,
				// and then check if it has changed the reference.
				// If it did not, everything is fine; meaning it must have accepted 'null' or whatever object is in there, or fixed its content.
				// If the reference was changed there is potentially some trouble.
				// If we're allowed to change it we use reflection, if not we throw an exception


				Expression onReassignment;
				if (readonlyFieldHandling == ReadonlyFieldHandling.ForcedOverwrite)
					// field.SetValue(valueArg, tempStore)
					onReassignment = Expression.Call(Expression.Constant(fieldInfo), _setValue, arg0: refValueArg, arg1: tempStore);
				else
					onReassignment = Expression.Throw(Expression.Constant(new CerasException("The reference in the readonly-field '" + fieldInfo.Name + "' would have to be overwritten, but forced overwriting is not enabled in the serializer settings. Either make the field writeable or enable ForcedOverwrite in the ReadonlyFieldHandling-setting.")));

				// Did the reference change?
				block.Add(Expression.IfThenElse(
									 test: Expression.ReferenceEqual(tempStore, Expression.MakeMemberAccess(refValueArg, fieldInfo)),

									 // Still the same. Whatever has happened (and there are a LOT of cases), it seems to be ok.
									 // Maybe the existing object's content was overwritten, or the instance reference was already as expected, or...
									 ifTrue: Expression.Empty(),

									 // Reference changed. Handle it depending on if its allowed or not
									 ifFalse: onReassignment
									));
			}
		}


		internal static void EmitBatchReadWrite()
		{
			// todo: sort structs that contain references to the end
			// todo: sort arrays to the end

			// Take all blittable things and emit a read/write to them directly
		}
	}


	struct MemberParameterPair
	{
		public MemberInfo Member;
		public ParameterExpression LocalVar;
	}
}