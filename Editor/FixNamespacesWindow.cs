﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Slothsoft.UnityExtensions.Editor {
    public class FixNamespacesWindow : EditorWindow {
        class NamespaceInfo {
            public DirectoryInfo directory;
            public DefaultAsset asset;
            public string classNamespace;
        }
        class ClassInfo {
            public NamespaceInfo ns;
            public FileInfo file;
            public MonoScript asset;
            public string contents;
            public bool isValid;
        }
        [MenuItem("Window/Slothsoft's Unity Extensions/Fix C# Namespaces")]
        public static void ShowWindow() {
            GetWindow<FixNamespacesWindow>();
        }

        AssemblyDefinitionAsset assembly {
            get => assemblyCache;
            set {
                if (assemblyCache != value) {
                    assemblyCache = value;
                    assemblyDirectory = new FileInfo(AssetDatabase.GetAssetPath(value)).Directory;
                    rootNamespace = PrefabUtils.GetNamespace(assembly);
                    namespaces = null;
                }
            }
        }
        AssemblyDefinitionAsset assemblyCache;
        DirectoryInfo assemblyDirectory;

        string rootNamespace {
            get => rootNamespaceCache;
            set {
                value = value.Replace("-", "");
                if (rootNamespaceCache != value) {
                    rootNamespaceCache = value;
                    namespaces = null;
                }
            }
        }
        string rootNamespaceCache = "";
        IEnumerable<NamespaceInfo> namespaces {
            get {
                if (namespacesCache == null && assemblyDirectory != null) {
                    namespacesCache = CreateNamespaceDictionary(assemblyDirectory, rootNamespace);
                }
                return namespacesCache;
            }
            set {
                if (namespacesCache != value) {
                    namespacesCache = value;
                    invalidFiles = null;
                }
            }
        }
        IEnumerable<NamespaceInfo> namespacesCache;
        IEnumerable<ClassInfo> invalidFiles {
            get {
                if (invalidFilesCache == null && namespaces != null) {
                    invalidFilesCache = FindInvalidFiles(namespaces);
                }
                return invalidFilesCache;
            }
            set => invalidFilesCache = value;
        }
        IEnumerable<ClassInfo> invalidFilesCache;

        bool listNamespaces = false;
        bool listFiles = false;

        public void OnGUI() {
            GUILayout.BeginVertical("box");
            GUILayout.Label("Fix C# Namespaces");
            assembly = EditorGUILayout.ObjectField("Assembly", assembly, typeof(AssemblyDefinitionAsset), false) as AssemblyDefinitionAsset;
            EditorGUILayout.LabelField(assemblyDirectory?.FullName);
            rootNamespace = EditorGUILayout.TextField("Root namespace", rootNamespace);

            listNamespaces = EditorGUILayout.Foldout(listNamespaces, "Namespaces in assembly");
            if (listNamespaces) {
                if (namespaces == null) {
                    EditorGUILayout.LabelField("Assign assembly first!");
                } else {
                    foreach (var ns in namespaces) {
                        EditorGUILayout.ObjectField(ns.classNamespace, ns.asset, ns.asset.GetType(), false);
                    }
                }
            }
            listFiles = EditorGUILayout.Foldout(listFiles, "Show invalid files");
            if (listFiles) {
                if (invalidFiles == null) {
                    EditorGUILayout.LabelField("Assign assembly first!");
                } else {
                    if (invalidFiles.Any()) {
                        foreach (var file in invalidFiles.Where(file => !file.isValid)) {
                            EditorGUILayout.ObjectField(file.ns.classNamespace, file.asset, file.asset.GetType(), false);
                        }
                    } else {
                        EditorGUILayout.LabelField("All namespace declarations match their folder!");
                    }
                }
            }
            if (GUILayout.Button("Fix namespaces in assembly")) {
                EditorUtils.ClearConsole();
                if (invalidFiles == null) {
                    Debug.LogError("Assign assembly first!");
                } else {
                    FixInvalidFiles(invalidFiles);
                }
            }
            GUILayout.EndVertical();
        }

        IEnumerable<NamespaceInfo> CreateNamespaceDictionary(DirectoryInfo assemblyDirectory, string rootNamespace) {
            var list = new Stack<NamespaceInfo>();
            void crawl(DirectoryInfo directory, string ns) {
                if (directory.GetFileSystemInfos().Any()) {
                    var asset = PrefabUtils.LoadAssetAtPath<DefaultAsset>(directory);
                    if (asset) {
                        list.Push(new NamespaceInfo {
                            asset = asset,
                            classNamespace = ns,
                            directory = directory,
                        });
                        foreach (var childDirectory in directory.GetDirectories()) {
                            crawl(childDirectory, $"{ns}.{childDirectory.Name}");
                        }
                    }
                }
            }
            crawl(assemblyDirectory, rootNamespace);
            return list.OrderBy(info => info.directory.FullName);
        }
        IEnumerable<ClassInfo> FindInvalidFiles(IEnumerable<NamespaceInfo> namespaces) {
            foreach (var ns in namespaces) {
                string usingDirective = string.Join("", namespaces.Except(new[] { ns }).Select(tmp => $"using {tmp.classNamespace};\r\n"));
                foreach (var file in ns.directory.EnumerateFiles("*.cs")) {
                    var asset = PrefabUtils.LoadAssetAtPath<MonoScript>(file);
                    if (asset) {
                        string contents = File.ReadAllText(file.FullName);
                        var rule = new Regex(@"namespace ([\w.]+)");
                        var match = rule.Match(contents);
                        if (match.Success) {
                            yield return new ClassInfo {
                                asset = asset,
                                file = file,
                                ns = ns,
                                isValid = match.Groups[1].Value == ns.classNamespace,
                                contents = usingDirective + rule.Replace(contents, $"namespace {ns.classNamespace}"),
                            };
                        }
                    }
                }
            }
        }
        void FixInvalidFiles(IEnumerable<ClassInfo> files) {
            if (files.Any(file => !file.isValid)) {
                foreach (var file in files) {
                    Debug.Log($"Writing class <b>{file.ns.classNamespace}.{file.asset.name}</b>...", file.asset);
                    File.WriteAllText(file.file.FullName, file.contents);
                }
            } else {
                Debug.Log("All files are valid, skipping fix!");
            }
        }
    }
}
