using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Microsoft.AspNetCore.Mvc.ApplicationParts
{
    public class AssemblyPartDiscoveryModel : IEquatable<AssemblyPartDiscoveryModel>
    {
        internal static readonly string[] ViewsAssemblySuffixes = new[]
        {
            ".PrecompiledViews",
            ".Views",
        };

        private readonly List<AssemblyPartDiscoveryModel> _additionalParts;

        public AssemblyPartDiscoveryModel(Assembly assembly)
            : this(assembly, assembly.GetName().Name, assembly.GetCustomAttributes(inherit: false))
        {
        }

        public AssemblyPartDiscoveryModel(
            Assembly assembly, 
            string name,
            IReadOnlyList<object> attributes)
        {
            Assembly = assembly;
            Name = name;
            Attributes = attributes;
            _additionalParts = new List<AssemblyPartDiscoveryModel>();
        }

        public string Name { get; }

        public Assembly Assembly { get; }

        public IReadOnlyList<object> Attributes { get; }

        public AssemblyPartDiscoveryModel Parent { get; private set; }

        public IReadOnlyList<AssemblyPartDiscoveryModel> AdditionalParts => _additionalParts;

        public void AddAdditionalPartModel(AssemblyPartDiscoveryModel additionalPart)
        {
            _additionalParts.Add(additionalPart);
            additionalPart.Parent = this;
        }

        public ApplicationPart ToApplicationPart()
        {
            for (var i = 0; i < ViewsAssemblySuffixes.Length; i++)
            {
                if (Name.EndsWith(ViewsAssemblySuffixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return new CompiledViewsApplicationPart(Assembly);
                }
            }

            return new AssemblyPart(Assembly);
        }

        public override string ToString() => Name;

        public static AssemblyPartDiscoveryModel ResolveEntryPoint(Assembly entryAssembly)
        {
            var lookup = new Dictionary<Assembly, AssemblyPartDiscoveryModel>();

            var entryAssemblyModel = ResolvePartModel(entryAssembly, lookup);
            var dependencyContextProvider = new DependencyContextPartDiscoveryProviider();
            var resolvedAssemblies = dependencyContextProvider.ResolveAssemblies(entryAssembly);

            foreach (var assembly in resolvedAssemblies)
            {
                if (assembly == entryAssembly)
                {
                    // Dependency context, if present, will resolve the current executing application
                    // Ignore this value since we've already created an entry for it.
                    continue;
                }

                var partModel = ResolvePartModel(assembly, lookup);
                entryAssemblyModel.AddAdditionalPartModel(partModel);
            }

            return entryAssemblyModel;
        }

        public static AssemblyPartDiscoveryModel ResolveAssemblyModel(Assembly assembly)
        {
            var lookup = new Dictionary<Assembly, AssemblyPartDiscoveryModel>();
            return ResolvePartModel(assembly, lookup);
        }

        internal static AssemblyPartDiscoveryModel ResolvePartModel(
            Assembly root,
            Dictionary<Assembly, AssemblyPartDiscoveryModel> lookup)
        {
            var visited = new HashSet<Assembly>();
            return ResolvePartModel(root);

            AssemblyPartDiscoveryModel ResolvePartModel(Assembly assembly)
            {
                if (!visited.Add(assembly))
                {
                    throw new InvalidOperationException("Recursion");
                }

                if (lookup.TryGetValue(assembly, out var resolvedModel))
                {
                    return resolvedModel;
                }

                var model = new AssemblyPartDiscoveryModel(assembly);

                var additionalParts = model.Attributes
                    .OfType<AdditionalApplicationPartAttribute>()
                    .ToArray();

                foreach (var additionalPart in additionalParts)
                {
                    var additionalPartAssembly = Assembly.Load(new AssemblyName(additionalPart.Name));
                    var additionalPartModel = ResolvePartModel(additionalPartAssembly);

                    model.AddAdditionalPartModel(additionalPartModel);
                }

                // If the assembly has signs of using any of the new load behavior primitives, don't 
                // auto-discover precompiled views for it.
                if (additionalParts.Length == 0 || model.Attributes.OfType<ConfigureApplicationPartManagerAttribute>().Any())
                {
                    var precompiledViewAssembly = GetPrecompiledViewsAssembly(assembly);
                    if (precompiledViewAssembly != null)
                    {
                        var partModel = ResolvePartModel(precompiledViewAssembly);
                        model.AddAdditionalPartModel(partModel);
                    }
                }

                return model;
            }
        }

        private static Assembly GetPrecompiledViewsAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                return null;
            }

            for (var i = 0; i < ViewsAssemblySuffixes.Length; i++)
            {
                var fileName = assembly.GetName().Name + ViewsAssemblySuffixes[i] + ".dll";
                var filePath = Path.Combine(Path.GetDirectoryName(assembly.Location), fileName);

                if (File.Exists(filePath))
                {
                    try
                    {
                        return Assembly.LoadFile(filePath);
                    }
                    catch (FileLoadException)
                    {
                        // Don't throw if assembly cannot be loaded. This can happen if the file is not a managed assembly.
                    }
                }
            }

            return null;
        }

        public override bool Equals(object obj)
        {
            return (obj is AssemblyPartDiscoveryModel model) && Equals(model);
        }

        public bool Equals(AssemblyPartDiscoveryModel other)
        {
            return Assembly.Equals(other?.Assembly);
        }

        public override int GetHashCode()
        {
            return Assembly.GetHashCode();
        }
    }
}
