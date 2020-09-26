# CSharpOutline2019

An extension for Visual Studio 2019 to add curly braces outlining for C# editor, **especially those braces in the catch & finally block**. It's better to disable built-in outlining.

>Tools-Option-Text Editor-C#-Advanced-Outlining, uncheck 'Show outlining of declaration level constructs' and 'Show outlining of code level constructs'

All the code of CSharpOutliningTagger comes from https://github.com/Skybladev2/C--outline-for-Visual-Studio , with some  changes.

![catch & finally block](demo.png)
</br></br>
## 2020-09-26 Update

- Implement tooltip with color and format which matches the theme of Visual Studio.  
 
</br>

![theme tooltip](themetooltip.png)

>Code of this part comes from [Roslyn](https://github.com/Trieste-040/https-github.com-dotnet-roslyn/blob/2d22d1aa4f1dfe3ae6f8de8cb7ddc218a5f1c4ff/src/EditorFeatures/Core/Implementation/Structure/BlockTagState.cs)