﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace p3dcppgen
{
    class Program
    {
        static readonly string classTemplate = @"
	class {0}
	{{
	public:

		{0}();

		{1}
	private:

		{2}
	}};";

        static readonly string ctorTemplate = @"
	{0}::{0}(const P3DChunk& chunk)
	{{
		assert(chunk.IsType(ChunkType::{0}));

		MemoryStream stream(chunk.GetData());

		{1}{2}	}}";

        static readonly string switchCaseTemplate = @"
		for (auto const& child : chunk.GetChildren())
		{{
			MemoryStream data(child->GetData());

			switch (child->GetType())
			{{
				{1}				default:
					std::cout << ""[{0}] Unexpected Chunk: "" << child->GetType() << ""\n"";
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

        static void Main(string[] args)
        {
            var defObject = JObject.Parse(File.ReadAllText("def.json"));
            var headersb = new StringBuilder();
            var cpprb = new StringBuilder();
            var forwardDecl = new List<string>();

            foreach (var classToken in defObject)
            {
                forwardDecl.Add(classToken.Key);

                var readers = new IndentedTextWriter(new StringWriter(), "\t") { Indent = 2 };
                var publicBlock = new IndentedTextWriter(new StringWriter(), "\t") { Indent = 2 };
                var privateBlock = new IndentedTextWriter(new StringWriter(), "\t") { Indent = 2 };
                var caseBlock = new IndentedTextWriter(new StringWriter(), "\t") { Indent = 4 };

                foreach (var classProperty in classToken.Value.Values<JProperty>())
                {
                    if (classProperty.Value.Type != JTokenType.String) continue;

                    var valueString = classProperty.Value.ToString();
                    if (string.IsNullOrWhiteSpace(valueString)) continue;

                    var propertyName = classProperty.Name.ToString();
                    if (string.IsNullOrWhiteSpace(propertyName)) continue;

                    var valueArgs = valueString.Split(new char[] { ' ', '<', '>' }, StringSplitOptions.RemoveEmptyEntries);
                    if (valueArgs.Length == 0) continue;

                    if (valueArgs.Length == 1)
                    {
                        var value = valueArgs[0];
                        var funcName = $"{Char.ToUpperInvariant(propertyName[0])}{propertyName.Substring(1)}";
                        var type = GetNativeType(value);
                        var readerName = value == "string" ? "LPString" : $"<{type}>";

                        publicBlock.WriteLine($"const {type}& {funcName}() const {{ return _{propertyName}; }}");
                        privateBlock.WriteLine($"{type} _{propertyName};");
                        readers.WriteLine($"_{propertyName} = stream.Read{readerName}();");
                    }
                    else if (valueArgs.Length <= 3)
                    {
                        var funcName = $"Get{Char.ToUpperInvariant(propertyName[0])}{propertyName.Substring(1)}";

                        switch (valueArgs[0])
                        {
                            case "child":
                                {
                                    if (valueArgs.Length != 2) break;
                                    var chunkType = valueArgs[1];

                                    publicBlock.WriteLine($"const unique_ptr<{chunkType}>& Get{funcName}() const {{ return _{propertyName}; }}");
                                    privateBlock.WriteLine($"unique_ptr<{chunkType}> _{propertyName};");

                                    caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                    	_{propertyName} = std::make_unique<{chunkType}>(*child);
                    	break;
                    }}");
                                    break;
                                }
                            case "children":
                                {
                                    if (valueArgs.Length != 2) break;
                                    var chunkType = valueArgs[1];

                                    publicBlock.WriteLine($"const std::vector<unique_ptr<{chunkType}>>& Get{funcName}() const {{ return _{propertyName}; }}");
                                    privateBlock.WriteLine($"std::vector<unique_ptr<{chunkType}>> _{propertyName};");

                                    caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                    	_{propertyName}.push_back(std::make_unique<{chunkType}>(*child));
                    	break;
                    }}");
                                    break;
                                }
                            case "buffer":
                                {
                                    if (valueArgs.Length != 3) break;
                                    var type = GetNativeType(valueArgs[1]);
                                    var chunkType = valueArgs[2];

                                    publicBlock.WriteLine($"const std::vector<{type}>& Get{funcName}() const {{ return _{propertyName}; }}");
                                    privateBlock.WriteLine($"std::vector<{type}> _{propertyName};");

                                    caseBlock.WriteLine($@"case ChunkType::{chunkType}:
                    {{
                    	uint32_t length = data.Read<uint32_t>();
                    	_{propertyName}.resize(length);
                    	data.ReadBytes(reinterpret_cast<uint8_t*>(_{propertyName}.data()), length * sizeof({type}));
                    	break;
                    }}");
                                    break;
                                }
                            case "buffers":
                                {
                                    if (valueArgs.Length != 3) break;
                                    var type = GetNativeType(valueArgs[1]);
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
                                    break;
                                }
                        }
                    }               
                }

                var switchBlock = new StringWriter();
                if (!string.IsNullOrEmpty(caseBlock.InnerWriter.ToString()))
                {
                    switchBlock.WriteLine(switchCaseTemplate, classToken.Key, caseBlock.InnerWriter);
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
                "string",
                "memory",
                "vector",
                "glm/vec2.hpp",
                "glm/vec3.hpp",
                "glm/vec4.hpp",
                "glm/gtc/quaternion.hpp",
                "glm/gtc/mat4x4.hpp",
            };

            var cppIncludes = new[]
            {
                "Core/MemoryStream.h",
                "iostream",
            };

            using (var writer = File.CreateText("p3d.generated.h"))
            {
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

            using (var writer = File.CreateText("p3d.generated.cpp"))
            {
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
        }
    }
}
