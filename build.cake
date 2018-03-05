var target = Argument("target", "Default");

const string name = "Righthand.Immutable";
const string libName = name + ".Library";
var src = Directory("src");
// src\Righthand.Immutable\Righthand.Immutable
var librarySlnDirectory = src + Directory(libName);
var libraryProjectDirectory = librarySlnDirectory + Directory(name);
var libraryProject = libraryProjectDirectory + File(name + ".csproj");

Task("BuildVsix")
    .Does(() => {
        
    });

Task("BuildNuGet")
    .Does(() => {
        var settings = new MSBuildSettings {
            Configuration = "Release"
        }.WithTarget("pack");
        MSBuild(libraryProject, settings);
    }
);

Task("Default")
    .IsDependentOn("BuildNuGet")
    .Does(() => {
       

    }
);

RunTarget(target);