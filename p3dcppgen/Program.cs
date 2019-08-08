﻿using CommandLine;

namespace DonutCodeGen
{
    class Program
    {
        public static readonly string GeneratedComment = @"/*+---------------------------------------------------+
  |    _____        /--------------------------\\     |
  |   /     \\      |                            |    |
  | \\/\\/     |    /  file generated by         |    |
  |  |  (o)(o)    |                  donut tool  |    |
  |  C   .---_)   \\_   _________________________/    |
  |   | |.___|      | /                               |
  |   |  \\__/      <_/                               |
  |   /_____\\                                        |
  |  /_____/ \\                                       |
  | /         \\                                      |
  +---------------------------------------------------+
*/";

        class Options
        {
            [Option("p3din")]
            public string P3DInputFile { get; set; }

            [Option("p3dout")]
            public string P3DOutputPath { get; set; }

            [Option("cmdin")]
            public string CmdInputFile { get; set; }

            [Option("cmdout")]
            public string CmdOutputPath { get; set; }

            [Option("copyright")]
            public string Copyright { get; set; }
        }

        static int Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            return result.MapResult(Process, errors => 1);
        }

        private static int Process(Options options)
        {
            int exitCode = 0;

            exitCode = P3DGenerator.Process(options.P3DInputFile, options.P3DOutputPath, options.Copyright);
            if (exitCode != 0) return exitCode;

            exitCode = CommandGenerator.Process(options.CmdInputFile, options.CmdOutputPath, options.Copyright);
            if (exitCode != 0) return exitCode;

            return exitCode;
        }
    }
}
