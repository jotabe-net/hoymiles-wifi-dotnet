# HoymilesProtobufClient

This folder contains the .NET implementation of the Hoymiles DTU client.

## Protobuf handling

The `.proto` files in `../hoymiles_wifi/protobuf` are the source of truth for the wire contract.

The C# project at `src/HoymilesProtobufClient/HoymilesProtobufClient` uses an explicit MSBuild target named `GenerateProtobufSources` to run `protoc` from the `Grpc.Tools` package and write generated C# files into `Generated/Proto` inside the project directory.

The intent is:

- keep the `.proto` files as the canonical schema
- keep the generated C# sources visible in the repo
- avoid hidden build-only output as much as possible

The generated files are compiled as normal C# source files by the project.

## Project layout

- `src/HoymilesProtobufClient/HoymilesProtobufClient` - class library project
- `src/HoymilesProtobufClient/HoymilesProtobufClient/Generated/Proto` - generated protobuf C# sources
- `../hoymiles_wifi/protobuf` - original `.proto` definitions copied from the Python project

## Build

Use `dotnet build` from the `dotnet/src/HoymilesProtobufClient/HoymilesProtobufClient` directory or build the solution from the `dotnet` folder.

## Notes

- `Grpc.Tools` is kept as a build-time dependency so the project can use its bundled `protoc` binary.
- No separate test project is included yet.
- The library is targeting .NET 10.
