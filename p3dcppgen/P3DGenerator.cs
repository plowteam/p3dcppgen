﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DonutCodeGen
{
    class P3DGenerator
    {
        static readonly string classTemplate = @"
    class {0}
    {{
    public:

        {0}(const P3DChunk&);

        static std::unique_ptr<{0}> Load(const P3DChunk& chunk) {{ return std::make_unique<{0}>(chunk); }}

        {1}
    private:

        {2}
    }};";

        static readonly string ctorTemplate = @"
    {0}::{0}(const P3DChunk& chunk)
    {{
        assert(chunk.IsType(ChunkType::{0}));

        MemoryStream stream(chunk.GetData());
        {1}{2}    }}";

        static readonly string switchCaseTemplate = @"
        for (auto const& child : chunk.GetChildren())
        {{
            {2}switch (child->GetType())
            {{
                {1}                default:
                    {3}break;
            }}
        }}";

        static readonly Dictionary<string, string> typeDict = new Dictionary<string, string>
        {
            { "s8", "int8_t" },
            { "s16", "int16_t" },
            { "s32", "int32_t" },
            { "s64", "int64_t" },

            { "u8", "uint8_t" },
            { "u16", "uint16_t" },
            { "u32", "uint32_t" },
            { "u64", "uint64_t" },

            { "bool", "bool" },
            { "float", "float" },
            { "string", "std::string" },

            { "vec2", "glm::vec2" },
            { "vec3", "glm::vec3" },
            { "vec4", "glm::vec4" },
            { "quat", "glm::quat" },
            { "mat4", "glm::mat4" },
        };

        static string GetNativeType(string s) => typeDict.TryGetValue(s, out var v) ? v : s;

        public static int Process(string inputFile, string outputPath, string copyright)
        {
            if (!File.Exists(inputFile) ||
                !Directory.Exists(outputPath))
            {
                return 1;
            }

            int exitCode = 0;

            var defObject = JObject.Parse(File.ReadAllText(inputFile));
            var headersb = new StringBuilder();
            var cpprb = new StringBuilder();
            var forwardDecl = new List<string>();

            foreach (var classToken in defObject)
            {
                forwardDecl.Add(classToken.Key);

                var readers = new IndentedTextWriter(new StringWriter()) { Indent = 2 };
                var publicBlock = new IndentedTextWriter(new StringWriter()) { Indent = 2 };
                var privateBlock = new IndentedTextWriter(new StringWriter()) { Indent = 2 };
                var caseBlock = new IndentedTextWriter(new StringWriter()) { Indent = 4 };
                bool useDataStream = false;
                bool useLogs = false;

                foreach (var classProperty in classToken.Value.Values<JProperty>())
                {
                    if (classProperty.Name == "!log")
                    {
                        useLogs = (bool)classProperty.Value;
                        continue;
                    }

                    if (classProperty.Value.Type != JTokenType.String) continue;

                    var valueString = classProperty.Value.ToString();
                    if (string.IsNullOrWhiteSpace(valueString)) continue;

                    var propertyName = classProperty.Name.ToString();
                    if (string.IsNullOrWhiteSpace(propertyName)) continue;

                    var valueArgs = valueString.Split(new char[] { ' ', '<', '>' }, StringSplitOptions.RemoveEmptyEntries);
                    if (valueArgs.Length == 0) continue;

                    if (valueArgs.Length == 1)
                    {
                        var type = valueArgs[0];
                        var funcName = $"{Char.ToUpperInvariant(propertyName[0])}{propertyName.Substring(1)}";
                        string nativeType = null;

                        if (type.Contains("["))
                        {
                            var split = type.Split(new char[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                            if (split.Length != 2 || !uint.TryParse(split[1], out var n)) continue;

                            nativeType = GetNativeType(split[0]);

                            publicBlock.WriteLine($"const {nativeType}& Get{funcName}() const {{ return _{propertyName}; }}");
                            privateBlock.WriteLine($"{nativeType} _{propertyName};");
                            readers.WriteLine($"_{propertyName} = stream.ReadString({n});");

                            continue;
                        }

                        nativeType = GetNativeType(type);
                        var readerName = type == "string" ? "LPString" : $"<{nativeType}>";

                        publicBlock.WriteLine($"const {nativeType}& Get{funcName}() const {{ return _{propertyName}; }}");
                        privateBlock.WriteLine($"{nativeType} _{propertyName};");
                        readers.WriteLine($"_{propertyName} = stream.Read{readerName}();");
                    }
                    else if (valueArgs.Length <= 4)
                    {
                        var funcName = $"{Char.ToUpperInvariant(propertyName[0])}{propertyName.Substring(1)}";

                        switch (valueArgs[0])
                        {
                            case "child":
                                {
                                    if (valueArgs.Length == 2)
                                    {
                                        var chunkType = valueArgs[1];

                                        publicBlock.WriteLine($"const std::unique_ptr<{chunkType}>& Get{funcName}() const {{ return _{propertyName}; }}");
                                        privateBlock.WriteLine($"std::unique_ptr<{chunkType}> _{propertyName};");

                                        caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        _{propertyName} = std::make_unique<{chunkType}>(*child);
                        break;
                    }}");
                                    }
                                    else if (valueArgs.Length == 3)
                                    {
                                        var type = valueArgs[1];
                                        var chunkType = valueArgs[2];
                                        var nativeType = GetNativeType(type);
                                        var readerName = type == "string" ? "LPString" : $"<{nativeType}>";

                                        publicBlock.WriteLine($"const {nativeType}& Get{funcName}() const {{ return _{propertyName}; }}");
                                        privateBlock.WriteLine($"{nativeType} _{propertyName};");

                                        caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        _{propertyName} = data.Read{readerName}();
                        break;
                    }}");
                                        useDataStream = true;
                                    }
                                    break;
                                }
                            case "children":
                                {
                                    if (valueArgs.Length == 2)
                                    {
                                        var chunkType = valueArgs[1];

                                        publicBlock.WriteLine($"const std::vector<std::unique_ptr<{chunkType}>>& Get{funcName}() const {{ return _{propertyName}; }}");
                                        privateBlock.WriteLine($"std::vector<std::unique_ptr<{chunkType}>> _{propertyName};");

                                        caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        _{propertyName}.push_back(std::make_unique<{chunkType}>(*child));
                        break;
                    }}");
                                    }
                                    else if (valueArgs.Length == 3)
                                    {
                                        var type = valueArgs[1];
                                        var chunkType = valueArgs[2];
                                        var nativeType = GetNativeType(type);
                                        var readerName = type == "string" ? "LPString" : $"<{nativeType}>";

                                        publicBlock.WriteLine($"const std::vector<{nativeType}>& Get{funcName}() const {{ return _{propertyName}; }}");
                                        privateBlock.WriteLine($"std::vector<{nativeType}> _{propertyName};");

                                        caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        _{propertyName}.push_back(data.Read{readerName}());
                        break;
                    }}");
                                        useDataStream = true;
                                    }
                                    break;
                                }
                            case "dictionary":
                                {
                                    if (valueArgs.Length == 4)
                                    {
                                        var keyType = GetNativeType(valueArgs[1]);
                                        var keyName = valueArgs[2];
                                        keyName = $"{Char.ToUpperInvariant(keyName[0])}{keyName.Substring(1)}";
                                        var chunkType = valueArgs[3];

                                        publicBlock.WriteLine($"{chunkType}* Get{funcName}Value(const {keyType}& key) const {{ auto it = _{propertyName}.find(key); return (it != _{propertyName}.end()) ? it->second.get() : nullptr; }}");
                                        privateBlock.WriteLine($"std::map<{keyType}, std::unique_ptr<{chunkType}>> _{propertyName};");

                                        caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        auto value = std::make_unique<{chunkType}>(*child);
                        _{propertyName}.insert({{ value->Get{keyName}(), std::move(value) }});
                        break;
                    }}");
                                    }
                                    break;
                                }
                            case "buffer":
                                {
                                    var type = valueArgs[1];
                                    string nativeType = "";
                                    string resizeString = "";
                                    bool fixedSize = false;

                                    if (type.Contains("["))
                                    {
                                        var split = type.Split(new char[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (split.Length != 2) continue;

                                        if (uint.TryParse(split[1], out var n))
                                        {
                                            resizeString = $"{n}";
                                        }
                                        else
                                        {
                                            resizeString = $"_{split[1]}";
                                        }

                                        nativeType = GetNativeType(split[0]);
                                        fixedSize = true;
                                    }
                                    else
                                    {
                                        nativeType = GetNativeType(type);
                                    }

                                    var isString = nativeType == GetNativeType("string");

                                    if (valueArgs.Length == 2)
                                    {
                                        if (!fixedSize) resizeString = "stream.Read<uint32_t>()";

                                        publicBlock.WriteLine($"const std::vector<{nativeType}>& Get{funcName}() const {{ return _{propertyName}; }}");
                                        privateBlock.WriteLine($"std::vector<{nativeType}> _{propertyName};");
                                        readers.WriteLine($"_{propertyName}.resize({resizeString});");

                                        if (isString)
                                        {
                                            readers.WriteLine($"for (size_t i = 0; i < _{propertyName}.size(); ++i)");
                                            readers.WriteLine("{");
                                            readers.WriteLine($"    _{propertyName}[i] = stream.ReadLPString();");
                                            readers.WriteLine("}");
                                        }
                                        else
                                        {
                                            readers.WriteLine($"stream.ReadBytes(reinterpret_cast<uint8_t*>(_{propertyName}.data()), _{propertyName}.size() * sizeof({nativeType}));");
                                        }
                                    }
                                    else if (valueArgs.Length == 3)
                                    {
                                        var chunkType = valueArgs[2];

                                        if (!fixedSize) resizeString = "data.Read<uint32_t>()";

                                        publicBlock.WriteLine($"const std::vector<{nativeType}>& Get{funcName}() const {{ return _{propertyName}; }}");
                                        privateBlock.WriteLine($"std::vector<{nativeType}> _{propertyName};");

                                        caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        _{propertyName}.resize({resizeString});
                        data.ReadBytes(reinterpret_cast<uint8_t*>(_{propertyName}.data()), _{propertyName}.size() * sizeof({nativeType}));
                        break;
                    }}");
                                        useDataStream = true;
                                    }
                                    break;
                                }
                            case "buffers":
                                {
                                    if (valueArgs.Length != 3) continue;
                                    var type = GetNativeType(valueArgs[1]);
                                    if (type == "string") break; // don't allow string buffers
                                    var chunkType = valueArgs[2];

                                    publicBlock.WriteLine($"const std::vector<{type}>& Get{funcName}(size_t index) const {{ return _{propertyName}.at(index); }}");
                                    privateBlock.WriteLine($"std::vector<std::vector<{type}>> _{propertyName};");

                                    caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                        uint32_t length = data.Read<uint32_t>();
                        uint32_t channel = data.Read<uint32_t>();
                        _{propertyName}.resize(channel + 1);
                        _{propertyName}.at(channel).resize(length);
                        data.ReadBytes(reinterpret_cast<uint8_t*>(_{propertyName}.at(channel).data()), length * sizeof({type}));
                        break;
                    }}");
                                    useDataStream = true;
                                    break;
                                }
                        }
                    }
                }

                var switchBlock = new StringWriter();
                if (!string.IsNullOrEmpty(caseBlock.InnerWriter.ToString()))
                {
                    switchBlock.WriteLine(switchCaseTemplate,
                        classToken.Key,
                        caseBlock.InnerWriter,
                        useDataStream ? "MemoryStream data(child->GetData());\n\n            " : null,
                        useLogs ? $"std::cout << \"[{classToken.Key}] Unexpected Chunk: \" << child->GetType() << \"\\n\";\n                    " : "");
                }

                headersb.AppendLine(string.Format(classTemplate,
                    classToken.Key,
                    publicBlock.InnerWriter,
                    privateBlock.InnerWriter));

                cpprb.AppendLine(string.Format(ctorTemplate,
                    classToken.Key,
                    readers.InnerWriter,
                    switchBlock));
            }

            var headerIncludes = new[]
            {
                "P3D/P3DChunk.h",
                "glm/vec2.hpp",
                "glm/vec3.hpp",
                "glm/vec4.hpp",
                "glm/gtc/quaternion.hpp",
                "glm/mat4x4.hpp",
                "string",
                "memory",
                "vector",
                "map",
            };

            var cppIncludes = new[]
            {
                "Core/MemoryStream.h",
                "iostream",
            };

            using (var writer = File.CreateText(Path.Combine(outputPath, "P3D.generated.h")))
            {
                writer.WriteLine($"// {copyright}\n");
                writer.WriteLine("#pragma once\n");
                writer.WriteLine(Program.GeneratedComment);
                writer.WriteLine();

                foreach (var inc in headerIncludes)
                {
                    writer.WriteLine($"#include <{inc}>");
                }

                writer.WriteLine();

                writer.WriteLine("namespace Donut::P3D");
                writer.WriteLine("{");
                foreach (var decl in forwardDecl)
                {
                    writer.WriteLine($"\tclass {decl};");
                }
                writer.Write(headersb.ToString());
                writer.WriteLine("}");
            }

            using (var writer = File.CreateText(Path.Combine(outputPath, "P3D.generated.cpp")))
            {
                writer.WriteLine($"// {copyright}\n");
                writer.WriteLine(Program.GeneratedComment);
                writer.WriteLine();

                writer.WriteLine("#include \"p3d.generated.h\"");
                foreach (var inc in cppIncludes)
                {
                    writer.WriteLine($"#include <{inc}>");
                }

                writer.WriteLine();

                writer.WriteLine("namespace Donut::P3D");
                writer.Write("{");
                writer.Write(cpprb.ToString());
                writer.WriteLine("}");
            }

            return exitCode;
        }
    }
}
