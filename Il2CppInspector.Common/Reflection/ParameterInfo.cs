﻿/*
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector.Reflection
{
    public class ParameterInfo
    {
        // IL2CPP-specific data
        public Il2CppParameterDefinition Definition { get; }
        public int Index { get; }
        public ulong DefaultValueMetadataAddress { get; }

        // Root definition: the parameter with Definition != null
        private readonly ParameterInfo rootDefinition;

        // Information/flags about the parameter
        public ParameterAttributes Attributes { get; }

        // Custom attributes for this parameter
        public IEnumerable<CustomAttributeData> CustomAttributes => Position == -1 /* Return type */
            ? CustomAttributeData.GetCustomAttributes(DeclaringMethod.Assembly, MetadataToken, 0)
            : CustomAttributeData.GetCustomAttributes(rootDefinition);

        // True if the parameter has a default value
        public bool HasDefaultValue => (Attributes & ParameterAttributes.HasDefault) != 0;

        // Default value for the parameter
        public object DefaultValue { get; }

        public bool IsIn => (Attributes & ParameterAttributes.In) != 0;
        public bool IsOptional => (Attributes & ParameterAttributes.Optional) != 0;
        public bool IsOut => (Attributes & ParameterAttributes.Out) != 0;
        public bool IsRetval => (Attributes & ParameterAttributes.Retval) != 0;

        // The method in which the parameter is defined
        public MethodBase DeclaringMethod { get; }

        // Metadata token of the parameter
        public int MetadataToken { get; }

        // Name of parameter
        public string Name { get; }
        public string CSharpName => Constants.Keywords.Contains(Name) ? "@" + Name : Name.ToCIdentifier();

        // Type of this parameter
        private readonly TypeRef paramTypeReference;
        public TypeInfo ParameterType => paramTypeReference.Value;

        // Zero-indexed position of the parameter in parameter list
        public int Position { get; }

        // Create a parameter. Specify paramIndex == -1 for a return type parameter
        public ParameterInfo(Il2CppInspector pkg, int paramIndex, MethodBase declaringMethod) {
            Index = paramIndex;
            DeclaringMethod = declaringMethod;

            if (paramIndex == -1) {
                Position = -1;
                paramTypeReference = TypeRef.FromReferenceIndex(declaringMethod.Assembly.Model, declaringMethod.Definition.ReturnType);
                MetadataToken = (int)declaringMethod.Definition.ReturnParameterToken;
                Attributes |= ParameterAttributes.Retval;
                return;
            }

            Definition = pkg.Params[Index];
            MetadataToken = (int) Definition.Token;
            Name = pkg.Strings[Definition.NameIndex];
            rootDefinition = this;

            // Handle unnamed/obfuscated parameter names
            if (string.IsNullOrEmpty(Name))
                Name = string.Format($"param_{Index:x8}");

            Position = paramIndex - declaringMethod.Definition.ParameterStart;
            paramTypeReference = TypeRef.FromReferenceIndex(declaringMethod.Assembly.Model, Definition.TypeIndex);

            var paramType = pkg.TypeReferences[Definition.TypeIndex];

            // Copy attributes
            Attributes = (ParameterAttributes) paramType.Attrs;

            // Default initialization value if present
            if (pkg.ParameterDefaultValue.TryGetValue(paramIndex, out (ulong address, object variant) value)) {
                DefaultValue = value.variant;
                DefaultValueMetadataAddress = value.address;
            }
        }

        // Create a concrete type parameter from a generic type parameter
        private ParameterInfo(ParameterInfo generic, MethodBase declaringMethod, TypeInfo concrete) {
            rootDefinition = generic.rootDefinition;

            DeclaringMethod = declaringMethod;
            Name = generic.Name;
            Position = generic.Position;
            Attributes = generic.Attributes;

            paramTypeReference = TypeRef.FromTypeInfo(concrete);

            DefaultValue = generic.DefaultValue;
            DefaultValueMetadataAddress = generic.DefaultValueMetadataAddress;
        }

        public ParameterInfo SubstituteGenericArguments(MethodBase declaringMethod, TypeInfo[] typeArguments, TypeInfo[] methodArguments = null) {
            TypeInfo t = ParameterType.SubstituteGenericArguments(typeArguments, methodArguments);
            if (t == ParameterType)
                return this;
            return new ParameterInfo(this, declaringMethod, t);
        }

        // ref will be handled as part of the type name
        public string GetModifierString() =>
              (IsIn && !IsOut ? "in " : "")
            + (IsOut && !IsIn ? "out " : "") 
            + (IsIn && IsOut  ? "[In, Out] " : "")
            + (!IsIn && !IsOut && ParameterType.IsByRef ? "ref " : "");

        private string getCSharpSignatureString(Scope scope) => $"{GetModifierString()}{ParameterType.GetScopedCSharpName(scope, omitRef: true)}";
        public string GetSignatureString() => $"{GetModifierString()}{ParameterType.FullName}";

        public string GetParameterString(Scope usingScope, bool emitPointer = false, bool compileAttributes = false) => IsRetval? null :
              $"{CustomAttributes.ToString(usingScope, inline: true, emitPointer: emitPointer, mustCompile: compileAttributes).Replace("[ParamArray]", "params")}"
            + (Position == 0 && DeclaringMethod.GetCustomAttributes("System.Runtime.CompilerServices.ExtensionAttribute").Any()? "this ":"")
            + $"{getCSharpSignatureString(usingScope)} {CSharpName}"
            + (IsOptional? " = " + DefaultValue.ToCSharpValue(ParameterType, usingScope) 
            + (emitPointer && !(DefaultValue is null)? $" /* Metadata: 0x{(uint) DefaultValueMetadataAddress:X8} */" : "") : "");

        public string GetReturnParameterString(Scope scope) => !IsRetval? null : getCSharpSignatureString(scope);

        public override string ToString() => ParameterType.Name + " " + Name;
    }
}