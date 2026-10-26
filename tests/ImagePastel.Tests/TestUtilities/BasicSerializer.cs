// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Xunit.Sdk;

namespace PinataLabs.ImagePastel.Tests.TestUtilities;

/// <summary>
/// RemoteExecutor can only execute static methods, which can only consume string arguments,
/// because data is being passed on command line interface. This utility allows serialization
/// of <see cref="IXunitSerializable"/> types to strings.
/// </summary>
/// <remarks>
/// <see cref="IXunitSerializationInfo.GetValue(string)"/> takes no type argument, so the type of
/// each value is stored next to it.
/// </remarks>
internal class BasicSerializer : IXunitSerializationInfo
{
    private readonly Dictionary<string, (Type Type, string Value)> map = new();

    public const char Separator = ':';

    private string DumpToString(Type type)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        writer.WriteLine(type.FullName);
        foreach (KeyValuePair<string, (Type Type, string Value)> kv in this.map)
        {
            writer.WriteLine($"{kv.Key}{Separator}{kv.Value.Type.AssemblyQualifiedName}{Separator}{kv.Value.Value}");
        }

        writer.Flush();
        byte[] data = ms.ToArray();
        return System.Convert.ToBase64String(data);
    }

    private Type LoadDump(string dump)
    {
        byte[] data = System.Convert.FromBase64String(dump);

        using var ms = new MemoryStream(data);
        using var reader = new StreamReader(ms);
        var type = Type.GetType(reader.ReadLine());
        for (string s = reader.ReadLine(); s != null; s = reader.ReadLine())
        {
            // Assembly qualified type names contain no separator, the value may.
            string[] parts = s.Split(Separator, 3);
            this.map[parts[0]] = (Type.GetType(parts[1], throwOnError: true), parts[2]);
        }

        return type;
    }

    public static string Serialize(IXunitSerializable serializable)
    {
        var serializer = new BasicSerializer();
        serializable.Serialize(serializer);
        return serializer.DumpToString(serializable.GetType());
    }

    public static T Deserialize<T>(string dump)
        where T : IXunitSerializable
    {
        var serializer = new BasicSerializer();
        Type type = serializer.LoadDump(dump);

        var result = (T)Activator.CreateInstance(type);
        result.Deserialize(serializer);
        return result;
    }

    public void AddValue(string key, object value, Type type = null)
    {
        Guard.NotNull(key, nameof(key));
        if (value == null)
        {
            return;
        }

        type ??= value.GetType();

        this.map[key] = (type, TypeDescriptor.GetConverter(type).ConvertToInvariantString(value));
    }

    public object GetValue(string key)
    {
        Guard.NotNull(key, nameof(key));

        if (!this.map.TryGetValue(key, out (Type Type, string Value) entry))
        {
            return null;
        }

        return TypeDescriptor.GetConverter(entry.Type).ConvertFromInvariantString(entry.Value);
    }
}
