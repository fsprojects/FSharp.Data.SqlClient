#!/bin/bash
if [ ! -f packages/FAKE/tools/FAKE.exe ]; then
  mono .nuget/NuGet.exe install FAKE -OutputDirectory packages -ExcludeVersion -Version 4.1.2 
fi
#workaround assembly resolution issues in build.fsx
mono packages/FAKE/tools/FAKE.exe build.fsx $@
