using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Alpaca4d.UIWidgets;
using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;
using Alpaca4d.Generic;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Steel section library based on embedded JSON database.
    /// Lets the user pick a section family and a specific section
    /// and returns an <see cref="Alpaca4d.Section.ISection"/> object.
    /// </summary>
    public class SectionLibrary : GH_ExtendableComponent
    {
        private MenuDropDown familyDrop;
        private MenuDropDown sectionDrop;

        private Dictionary<string, List<string>> familyToSections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly object jsonLock = new object();
        private static JObject steelSectionDb;

        // Persisted selection state (for GH file save/load)
        private string storedFamily = null;
        private string storedSection = null;

        public override Guid ComponentGuid => new Guid("{0F82C248-4B58-4C25-9F85-6B7F7C4696C0}");
        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.Section_Library__Alpaca4d_;

        public SectionLibrary()
            : base("Steel Section Library (Alpaca4d)", "Section Library",
                  "Select a steel section family (I, O, [], 2L) and section from the embedded database",
                  "Alpaca4d", "01_Section")
        {
            // Draw a Description underneath the component
            this.Message = ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Section name input (optional) - allows user to type section name directly
            pManager.AddTextParameter("Section Name", "Name", "Type section name directly (e.g., 'IPE200'). Overrides dropdown selection.", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            
            // Material is optional, defaulting to elastic steel if not supplied
            pManager.AddGenericParameter("Material", "Material", "Section material (defaults to Elastic Steel)", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            
            // Gap parameter for double L-sections (2L) will be added dynamically
            // when the 2L family is selected
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Section", "Section", "Steel section from the library (I, O, [], or 2L)");
        }

        #region UI setup

        protected override void Setup(GH_ExtendableComponentAttributes attr)
        {
            var menu = new GH_ExtendableMenu(0, "Library");
            menu.Name = "Library";
            menu.Header = "Select family and section";

            var panel = new MenuPanel(0, "Library");
            panel.Header = "Library";

            familyDrop = new MenuDropDown(0, "Family", "Family");
            familyDrop.VisibleItemCount = 8;
            familyDrop.ValueChanged += OnFamilyChanged;

            sectionDrop = new MenuDropDown(1, "Section", "Section");
            sectionDrop.VisibleItemCount = 12;
            sectionDrop.ValueChanged += OnSectionChanged;

            panel.AddControl(familyDrop);
            panel.AddControl(sectionDrop);
            menu.AddControl(panel);
            menu.Expand();
            attr.AddMenu(menu);
            attr.MinWidth = 180f;

            // Populate dropdown items
            InitializeFamilyOptions();

            // Apply any stored selections (for loaded components) and
            // also build the section list for the selected family.
            RestoreSelectionsFromStoredValues();
            
            // Update Gap parameter visibility based on selected family
            UpdateGapParameterVisibility();
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();

            // Rebuild dropdown contents and then reapply the stored selection
            InitializeFamilyOptions();
            RestoreSelectionsFromStoredValues();
            
            // Update Gap parameter visibility based on selected family
            UpdateGapParameterVisibility();
        }

        private void OnFamilyChanged(object sender, EventArgs e)
        {
            InitializeSectionOptions();
            UpdateGapParameterVisibility();
            this.ExpireSolution(true);
        }

        private void OnSectionChanged(object sender, EventArgs e)
        {
            this.ExpireSolution(true);
        }

        /// <summary>
        /// Dynamically add or remove the Gap parameter based on the selected family.
        /// The Gap parameter is only shown when the 2L family is selected.
        /// </summary>
        private void UpdateGapParameterVisibility()
        {
            string selFamily = familyDrop != null
                ? GetSelected(familyDrop, "I")  // Default to "I" family
                : "I";

            bool is2LFamily = selFamily.Equals("2L", StringComparison.OrdinalIgnoreCase);
            
            // Check if Gap parameter exists by looking for it by name
            IGH_Param gapParam = Params.Input.FirstOrDefault(p => p.Name == "Gap");
            bool hasGapParameter = gapParam != null;

            if (is2LFamily && !hasGapParameter)
            {
                // Add Gap parameter after Section Name (at index 2)
                var newGapParam = new Grasshopper.Kernel.Parameters.Param_Number();
                newGapParam.Name = "Gap";
                newGapParam.NickName = "Gap";
                newGapParam.Description = "Gap between two L-sections (in meters)";
                newGapParam.Access = GH_ParamAccess.item;
                newGapParam.Optional = true;
                newGapParam.SetPersistentData(0.01); // Default value 10mm
                
                Params.RegisterInputParam(newGapParam, 2);
                Params.OnParametersChanged();
                OnDisplayExpired(true);
            }
            else if (!is2LFamily && hasGapParameter)
            {
                // Remove Gap parameter
                Params.UnregisterInputParameter(gapParam, true);
                Params.OnParametersChanged();
                OnDisplayExpired(true);
            }
        }

        #endregion

        #region JSON loading helpers

        private static JObject LoadJsonResourceOnce(ref JObject cache, string resourceName)
        {
            if (cache != null) return cache;
            lock (jsonLock)
            {
                if (cache != null) return cache;
                var coreAsm = typeof(Alpaca4d.Section.ISection).Assembly;
                using (var stream = coreAsm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) throw new FileNotFoundException("Embedded resource not found", resourceName);
                    using (var reader = new StreamReader(stream))
                    {
                        var json = reader.ReadToEnd();
                        cache = JObject.Parse(json);
                        return cache;
                    }
                }
            }
        }

        private static double TryGetDouble(JObject obj, string key)
        {
            if (obj == null) return double.NaN;
            var token = obj[key];
            return token != null ? token.Value<double>() : double.NaN;
        }

        #endregion

        #region Dropdown population

        private void InitializeFamilyOptions()
        {
            // Build the dynamic dictionary from embedded JSON resource
            familyToSections = BuildFamilyToSections();

            if (familyDrop == null) return;
            familyDrop.Clear();

            // Sort families with "I" first, then alphabetically for the rest
            var families = familyToSections.Keys.OrderBy(k => k == "I" ? "0" : k, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < families.Count; i++)
            {
                var f = families[i];
                familyDrop.AddItem(f, f);
            }
            
            // Set "I" as the default selection if no stored family exists
            if (string.IsNullOrEmpty(storedFamily) && familyDrop.Items.Count > 0)
            {
                familyDrop.Value = 0; // "I" will be at index 0
            }
        }

        private Dictionary<string, List<string>> BuildFamilyToSections()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var db = LoadJsonResourceOnce(ref steelSectionDb, "Alpaca4d.Resources.Section.section.json");
            if (db == null) return result;

            void AddSection(string familyName, string sectionName)
            {
                if (string.IsNullOrWhiteSpace(sectionName)) return;
                if (string.IsNullOrWhiteSpace(familyName)) familyName = "Unknown";

                if (!result.TryGetValue(familyName, out var list))
                {
                    list = new List<string>();
                    result[familyName] = list;
                }

                if (!list.Contains(sectionName))
                    list.Add(sectionName);
            }

            // Two supported schemas:
            // 1) Families at top level: { "IPE": { "IPE80": {..}, ... }, "HEA": {...}, ... }
            // 2) Single top-level group (e.g. "I") with all sections inside. We then
            //    derive the family from the alphanumeric prefix of the section name
            //    (IPE, HEA, HEB, etc.).
            var topLevelProps = db.Properties().ToList();
            if (topLevelProps.Count == 1 && topLevelProps[0].Value is JObject singleFamilyObj)
            {
                foreach (var secProp in singleFamilyObj.Properties())
                {
                    var secName = secProp.Name;
                    var famName = ExtractFamilyFromSectionName(secName);
                    AddSection(famName, secName);
                }
            }
            else
            {
                foreach (var famProp in topLevelProps)
                {
                    if (famProp.Value is JObject familyObj)
                    {
                        foreach (var secProp in familyObj.Properties())
                        {
                            AddSection(famProp.Name, secProp.Name);
                        }
                    }
                }
            }

            // Sort section lists
            foreach (var kv in result)
            {
                kv.Value.Sort(StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }

        private static string ExtractFamilyFromSectionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            int i = 0;
            while (i < name.Length && !char.IsDigit(name[i]))
                i++;
            return i > 0 ? name.Substring(0, i) : "Unknown";
        }

        private void InitializeSectionOptions()
        {
            if (familyDrop == null || sectionDrop == null) return;
            string selFamily = GetSelected(familyDrop, defaultName: familyToSections.Keys.FirstOrDefault() ?? "Unknown");

            sectionDrop.Clear();
            if (familyToSections.TryGetValue(selFamily, out var sections))
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    var s = sections[i];
                    sectionDrop.AddItem(s, s);
                }
            }
        }

        /// <summary>
        /// Apply the stored selection (family/section) to the dropdowns after they
        /// have been populated. Safe to call when any of the dropdowns are null.
        /// </summary>
        private void RestoreSelectionsFromStoredValues()
        {
            // Restore family selection
            if (familyDrop != null && !string.IsNullOrEmpty(storedFamily))
            {
                int familyIdx = familyDrop.FindIndex(storedFamily);
                if (familyIdx >= 0)
                    familyDrop.Value = familyIdx;
            }

            // Rebuild and restore section selection based on the (possibly updated) family
            if (sectionDrop != null)
            {
                InitializeSectionOptions();

                if (!string.IsNullOrEmpty(storedSection))
                {
                    int secIdx = sectionDrop.FindIndex(storedSection);
                    if (secIdx >= 0)
                        sectionDrop.Value = secIdx;
                }
            }
        }

        private static string GetSelected(MenuDropDown dd, string defaultName)
        {
            if (dd.Items.Count == 0) return defaultName;
            int idx = Math.Max(0, Math.Min(dd.Value, dd.Items.Count - 1));
            return dd.Items[idx].name ?? dd.Items[idx].content ?? defaultName;
        }

        #endregion

        #region SolveInstance

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Section Name input (optional) - typed section name overrides dropdown
            string typedSectionName = null;
            try { DA.GetData(0, ref typedSectionName); } catch { }

            // Material input (optional)
            IUniaxialMaterial material = Alpaca4d.Material.UniaxialMaterialElastic.Steel;
            if (Params.Input.Count > 1)
            {
                try { DA.GetData(1, ref material); } catch { }
            }

            // Gap input (optional, for L-sections)
            double gap = 0.01; // Default 10mm gap
            var gapParamIndex = Params.Input.FindIndex(p => p.Name == "Gap");
            if (gapParamIndex >= 0)
            {
                try { DA.GetData(gapParamIndex, ref gap); } catch { }
            }

            // Current selections from UI
            string selFamily = familyDrop != null
                ? GetSelected(familyDrop, "I")
                : "I";

            string selSection = sectionDrop != null
                ? GetSelected(sectionDrop, null)
                : null;

            // If user provided a typed section name, use that and update the dropdowns
            if (!string.IsNullOrWhiteSpace(typedSectionName))
            {
                var searchResult = FindSectionInDatabase(typedSectionName.Trim());
                if (searchResult.found)
                {
                    selFamily = searchResult.family;
                    selSection = searchResult.sectionName;
                    
                    // Update the UI dropdowns to reflect the typed input
                    UpdateDropdownsFromTypedInput(selFamily, selSection);
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Section '{typedSectionName}' not found in the steel section database. Please check the spelling or use the dropdown menu.");
                    return;
                }
            }

            if (string.IsNullOrEmpty(selSection))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No section is selected. Please choose a section from the library or type a section name.");
                return;
            }

            // Keep the persisted selection state in sync with the UI
            storedFamily = selFamily;
            storedSection = selSection;

            // Create the appropriate section type based on the family
            IUniaxialSection section = CreateSection(selFamily, selSection, material, gap);
            if (section == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Section '{selSection}' not found in the steel section database.");
                return;
            }

            DA.SetData(0, section);
        }

        /// <summary>
        /// Creates the appropriate section type based on the family.
        /// Returns CircleCS for O family, RectangleHollowCS for [] family, 
        /// DoubleLAngleCS for 2L family, and ISection for I family.
        /// </summary>
        private IUniaxialSection CreateSection(string family, string sectionName, IUniaxialMaterial material, double gap)
        {
            var db = LoadJsonResourceOnce(ref steelSectionDb, "Alpaca4d.Resources.Section.section.json");
            if (db == null) return null;

            JObject sectionEntry = FindSectionEntry(db, family, sectionName);
            if (sectionEntry == null) return null;

            const double mmToM = 0.001;

            if (family.Equals("O", StringComparison.OrdinalIgnoreCase))
            {
                // Circular hollow section: d (diameter), t (thickness)
                var d = TryGetDouble(sectionEntry, "d");
                var t = TryGetDouble(sectionEntry, "t");

                if (double.IsNaN(d) || double.IsNaN(t))
                    return null;

                return new Alpaca4d.Section.CircleCS(sectionName, d * mmToM, t * mmToM, material);
            }
            else if (family.Equals("[]", StringComparison.Ordinal))
            {
                // Rectangular hollow section: h, b, t
                var h = TryGetDouble(sectionEntry, "h");
                var b = TryGetDouble(sectionEntry, "b");
                var t = TryGetDouble(sectionEntry, "t");

                if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(t))
                    return null;

                // RectangleHollowCS constructor: (secName, width, height, web, topFlange, bottomFlange, material)
                // For rectangular hollow sections with uniform thickness, all wall thicknesses are the same
                return new Alpaca4d.Section.RectangleHollowCS(sectionName, b * mmToM, h * mmToM, t * mmToM, t * mmToM, t * mmToM, material);
            }
            else if (family.Equals("2L", StringComparison.OrdinalIgnoreCase))
            {
                // Double L-section (angle): h, b, t
                var h = TryGetDouble(sectionEntry, "h");
                var b = TryGetDouble(sectionEntry, "b");
                var t = TryGetDouble(sectionEntry, "t");

                if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(t))
                    return null;

                // DoubleLAngleCS constructor: (secName, height, width, thickness, gap, material)
                return new Alpaca4d.Section.DoubleLAngleCS(sectionName, h * mmToM, b * mmToM, t * mmToM, gap, material);
            }
            else
            {
                // I-section or similar: h, b, tw, tf
                var h = TryGetDouble(sectionEntry, "h");
                var b = TryGetDouble(sectionEntry, "b");
                var tw = TryGetDouble(sectionEntry, "tw");
                var tf = TryGetDouble(sectionEntry, "tf");

                if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(tw) || double.IsNaN(tf))
                    return null;

                return new Alpaca4d.Section.ISection(sectionName, h * mmToM, b * mmToM, tf * mmToM, b * mmToM, tf * mmToM, tw * mmToM, material);
            }
        }


        private static JObject FindSectionEntry(JObject db, string family, string sectionName)
        {
            if (db == null || string.IsNullOrEmpty(sectionName)) return null;

            // First try schema with explicit families: db[family][sectionName]
            if (!string.IsNullOrEmpty(family))
            {
                var famToken = db[family] as JObject;
                if (famToken != null)
                {
                    var entry = famToken[sectionName] as JObject;
                    if (entry != null) return entry;
                }
            }

            // Fallback: if there's a single top-level group (e.g. "I"), search inside it.
            var singleGroup = db.Properties().FirstOrDefault()?.Value as JObject;
            var candidate = singleGroup?[sectionName] as JObject;
            if (candidate != null) return candidate;

            // As a last resort, search all families for a matching section name.
            foreach (var famProp in db.Properties())
            {
                if (famProp.Value is JObject famObj)
                {
                    var entry = famObj[sectionName] as JObject;
                    if (entry != null) return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// Searches for a section by name in the database and returns the family and section name.
        /// Performs case-insensitive search across all families.
        /// </summary>
        private (bool found, string family, string sectionName) FindSectionInDatabase(string searchName)
        {
            if (string.IsNullOrWhiteSpace(searchName))
                return (false, null, null);

            // First, try to find in the cached family-to-sections dictionary
            foreach (var kvp in familyToSections)
            {
                var match = kvp.Value.FirstOrDefault(s => 
                    s.Equals(searchName, StringComparison.OrdinalIgnoreCase));
                
                if (match != null)
                {
                    return (true, kvp.Key, match);
                }
            }

            // If not found in cache, search directly in the JSON database
            var db = LoadJsonResourceOnce(ref steelSectionDb, "Alpaca4d.Resources.Section.section.json");
            if (db == null) return (false, null, null);

            // Search in all top-level properties
            foreach (var famProp in db.Properties())
            {
                if (famProp.Value is JObject familyObj)
                {
                    foreach (var secProp in familyObj.Properties())
                    {
                        if (secProp.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Determine the family name
                            string familyName = famProp.Name;
                            
                            // If the top level is a single family group (like "I"), 
                            // extract family from section name
                            if (db.Properties().Count() == 1)
                            {
                                familyName = ExtractFamilyFromSectionName(secProp.Name);
                            }
                            
                            return (true, familyName, secProp.Name);
                        }
                    }
                }
            }

            return (false, null, null);
        }

        /// <summary>
        /// Updates the dropdown menus to reflect a section that was typed by the user.
        /// This provides visual feedback that the typed input was recognized.
        /// </summary>
        private void UpdateDropdownsFromTypedInput(string family, string sectionName)
        {
            if (familyDrop == null || sectionDrop == null) return;

            // Update family dropdown
            int familyIdx = familyDrop.FindIndex(family);
            if (familyIdx >= 0)
            {
                familyDrop.Value = familyIdx;
            }

            // Rebuild section dropdown for the selected family
            InitializeSectionOptions();

            // Update section dropdown
            int sectionIdx = sectionDrop.FindIndex(sectionName);
            if (sectionIdx >= 0)
            {
                sectionDrop.Value = sectionIdx;
            }

            // Update Gap parameter visibility if family changed
            UpdateGapParameterVisibility();
        }

        #endregion

        #region GH persistence

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            // Persist the last used selections so the UI can restore them.
            var chunk = writer.CreateChunk("SectionLibrary");
            if (!string.IsNullOrEmpty(storedFamily))
                chunk.SetString("Family", storedFamily);
            if (!string.IsNullOrEmpty(storedSection))
                chunk.SetString("Section", storedSection);

            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            // Load stored selections (if present) before the base logic triggers OnComponentLoaded.
            // Support both old "SectionPreset" and new "SectionLibrary" chunk names for backward compatibility
            string chunkName = reader.ChunkExists("SectionLibrary") ? "SectionLibrary" : "SectionPreset";
            
            if (reader.ChunkExists(chunkName))
            {
                var chunk = reader.FindChunk(chunkName);
                try { storedFamily = chunk.GetString("Family"); } catch { storedFamily = null; }
                try { storedSection = chunk.GetString("Section"); } catch { storedSection = null; }
            }

            return base.Read(reader);
        }

        #endregion
    }
}

