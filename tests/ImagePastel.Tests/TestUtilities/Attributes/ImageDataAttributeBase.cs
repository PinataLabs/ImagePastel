// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace PinataLabs.ImagePastel.Tests;

/// <summary>
/// Base class for Theory Data attributes which pass an instance of <see cref="TestImageProvider{TPixel}"/> to the test case.
/// </summary>
public abstract class ImageDataAttributeBase : DataAttribute
{
    protected readonly object[] AdditionalParameters;

    protected readonly PixelTypes PixelTypes;

    static ImageDataAttributeBase()
    {
        // ImageDataAttributes are used in almost all tests, thus a good place to enforce the execution of
        // TestEnvironment static constructor before anything else is done.
        TestEnvironment.EnsureSharedInitializersDone();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageDataAttributeBase"/> class.
    /// </summary>
    protected ImageDataAttributeBase(string memberName, PixelTypes pixelTypes, object[] additionalParameters)
    {
        this.PixelTypes = pixelTypes;

        // A single null argument binds to the params array itself, e.g. [WithFile(file, pixelTypes, null)].
        // xUnit v2 rebuilt the arguments from attribute metadata and passed it as one null value;
        // v3 uses the attribute instance as-is, so restore that meaning here.
        this.AdditionalParameters = additionalParameters ?? new object[] { null };
        this.MemberName = memberName;
    }

    /// <summary>
    /// Gets the member name.
    /// </summary>
    public string MemberName { get; }

    /// <summary>
    /// Gets or sets the member type.
    /// </summary>
    public Type MemberType { get; set; }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ITheoryDataRow[] rows = this.GetDataRows(testMethod).Select(this.ConvertDataRow).ToArray();
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <summary>
    /// Gets a value indicating whether the data can be enumerated during discovery.
    /// Every row is deterministic and every <see cref="TestImageProvider{TPixel}"/> is serializable,
    /// so each row is discovered as its own test case.
    /// </summary>
    /// <returns>Always <see langword="true"/>.</returns>
    public override bool SupportsDiscoveryEnumeration() => true;

    /// <summary>Returns the data to be used to test the theory.</summary>
    /// <param name="testMethod">The method that is being tested</param>
    /// <returns>One or more sets of theory data. Each invocation of the test method
    /// is represented by a single object array.</returns>
    private IEnumerable<object[]> GetDataRows(MethodInfo testMethod)
    {
        IEnumerable<object[]> addedRows = Enumerable.Empty<object[]>().ToArray();
        if (!string.IsNullOrWhiteSpace(this.MemberName))
        {
            Type type = this.MemberType ?? testMethod.DeclaringType;
            Func<object> accessor = this.GetPropertyAccessor(type, this.MemberName) ?? this.GetFieldAccessor(type, this.MemberName);

            if (accessor != null)
            {
                object obj = accessor();
                if (obj is IEnumerable<object> memberItems)
                {
                    // TheoryData yields ITheoryDataRow instances rather than object arrays.
                    addedRows = memberItems.Select(x => x is ITheoryDataRow row ? row.GetData() : x as object[]).ToArray();
                    if (addedRows.Any(x => x == null))
                    {
                        addedRows = memberItems.Select(x => new[] { x }).ToArray();
                    }
                }
            }
        }

        if (!addedRows.Any())
        {
            addedRows = new[] { Array.Empty<object>() };
        }

        bool firstIsProvider = this.FirstIsProvider(testMethod);
        if (firstIsProvider)
        {
            return this.InnerGetData(testMethod, addedRows);
        }

        return addedRows.Select(x => x.Concat(this.AdditionalParameters).ToArray());
    }

    /// <summary>
    /// Returns a value indicating whether the first parameter of the method is a test provider.
    /// </summary>
    /// <returns>True, if the first parameter is a test provider.</returns>
    private bool FirstIsProvider(MethodInfo testMethod)
    {
        TypeInfo dataType = testMethod.GetParameters().First().ParameterType.GetTypeInfo();
        return dataType.IsGenericType && dataType.GetGenericTypeDefinition() == typeof(TestImageProvider<>);
    }

    private IEnumerable<object[]> InnerGetData(MethodInfo testMethod, IEnumerable<object[]> memberData)
    {
        foreach (KeyValuePair<PixelTypes, Type> kv in this.PixelTypes.ExpandAllTypes())
        {
            Type factoryType = typeof(TestImageProvider<>).MakeGenericType(kv.Value);

            foreach (object[] originalFactoryMethodArgs in this.GetAllFactoryMethodArgs(testMethod, factoryType))
            {
                foreach (object[] row in memberData)
                {
                    object[] actualFactoryMethodArgs = new object[originalFactoryMethodArgs.Length + 2];
                    Array.Copy(originalFactoryMethodArgs, actualFactoryMethodArgs, originalFactoryMethodArgs.Length);
                    actualFactoryMethodArgs[actualFactoryMethodArgs.Length - 2] = testMethod;
                    actualFactoryMethodArgs[actualFactoryMethodArgs.Length - 1] = kv.Key;

                    object factory = factoryType.GetMethod(this.GetFactoryMethodName(testMethod))
                        .Invoke(null, actualFactoryMethodArgs);

                    object[] result = new object[this.AdditionalParameters.Length + 1 + row.Length];
                    result[0] = factory;
                    Array.Copy(row, 0, result, 1, row.Length);
                    Array.Copy(this.AdditionalParameters, 0, result, 1 + row.Length, this.AdditionalParameters.Length);
                    yield return result;
                }
            }
        }
    }

    /// <summary>
    /// Generates the collection of method arguments from the given test as a generic enumerable.
    /// </summary>
    /// <param name="testMethod">The test method</param>
    /// <param name="factoryType">The test image provider factory type</param>
    /// <returns>The <see cref="IEnumerable{T}"/></returns>
    protected virtual IEnumerable<object[]> GetAllFactoryMethodArgs(MethodInfo testMethod, Type factoryType)
    {
        object[] args = this.GetFactoryMethodArgs(testMethod, factoryType);
        return Enumerable.Repeat(args, 1);
    }

    /// <summary>
    /// Generates the collection of method arguments from the given test.
    /// </summary>
    /// <param name="testMethod">The test method</param>
    /// <param name="factoryType">The test image provider factory type</param>
    /// <returns>The <see cref="T:object[]"/></returns>
    protected virtual object[] GetFactoryMethodArgs(MethodInfo testMethod, Type factoryType)
    {
        throw new InvalidOperationException("Semi-abstract method");
    }

    /// <summary>
    /// Generates the method name from the given test method.
    /// </summary>
    /// <param name="testMethod">The test method</param>
    /// <returns>The <see cref="string"/></returns>
    protected abstract string GetFactoryMethodName(MethodInfo testMethod);

    /// <summary>
    /// Gets the field accessor for the given type.
    /// </summary>
    /// <returns>The field accessor.</returns>
    protected Func<object> GetFieldAccessor(Type type, string memberName)
    {
        FieldInfo fieldInfo = null;
        for (Type reflectionType = type; reflectionType != null; reflectionType = reflectionType.GetTypeInfo().BaseType)
        {
            fieldInfo = reflectionType.GetRuntimeField(memberName);
            if (fieldInfo != null)
            {
                break;
            }
        }

        if (fieldInfo is null || !fieldInfo.IsStatic)
        {
            return null;
        }

        return () => fieldInfo.GetValue(null);
    }

    /// <summary>
    /// Gets the property accessor for the given type.
    /// </summary>
    /// <returns>The property accessor.</returns>
    protected Func<object> GetPropertyAccessor(Type type, string memberName)
    {
        PropertyInfo propInfo = null;
        for (Type reflectionType = type; reflectionType != null; reflectionType = reflectionType.GetTypeInfo().BaseType)
        {
            propInfo = reflectionType.GetRuntimeProperty(memberName);
            if (propInfo != null)
            {
                break;
            }
        }

        if (propInfo?.GetMethod is null || !propInfo.GetMethod.IsStatic)
        {
            return null;
        }

        return () => propInfo.GetValue(null, null);
    }
}
