# JournalReader

Provide a simple way to read Windows USN change journal. This program is meant to be used as an example of how to use the USN change journal API from a managed code. It is not meant to be used in production.


## Requirements
- .NET >= 6

## How to build/run

*Build*:
- `dotnet build`
   - To build with file name retrieval disabled, add `/p:ExtraDefineConstants=NO_FILENAME`

*Run*:
- `cd JournalReader`
- `dotnet run help` (see the help message for more details)