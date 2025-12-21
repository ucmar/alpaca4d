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
    public class SectionPresetSteel : GH_ExtendableComponent
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
        protected override Bitmap Icon => Alpaca4d.Gh.Properties.Resources.I_Section__Alpaca4d_;

        public SectionPresetSteel()
            : base("Steel Section Library (Alpaca4d)", "Section Library",
                  "Select a steel section family and section from the embedded database",
                  "Alpaca4d", "01_Section")
        {
            // Draw a Description underneath the component
            this.Message = ComponentMessage.MyMessage(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Material is optional, defaulting to elastic steel if not supplied
            pManager.AddGenericParameter("Material", "Material", "Section material (defaults to Elastic Steel)", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.Register_GenericParam("Section", "Section", "Steel I section from the library");
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
        }

        protected override void OnComponentLoaded()
        {
            base.OnComponentLoaded();

            // Rebuild dropdown contents and then reapply the stored selection
            InitializeFamilyOptions();
            RestoreSelectionsFromStoredValues();
        }

        private void OnFamilyChanged(object sender, EventArgs e)
        {
            InitializeSectionOptions();
            this.ExpireSolution(true);
        }

        private void OnSectionChanged(object sender, EventArgs e)
        {
            this.ExpireSolution(true);
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

            var families = familyToSections.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < families.Count; i++)
            {
                var f = families[i];
                familyDrop.AddItem(f, f);
            }
        }

        private Dictionary<string, List<string>> BuildFamilyToSections()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var db = LoadJsonResourceOnce(ref steelSectionDb, "Alpaca4d.Resources.Section.steel_section.json");
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
            // Material input (optional)
            IUniaxialMaterial material = Alpaca4d.Material.UniaxialMaterialElastic.Steel;
            if (Params.Input.Count > 0)
            {
                try { DA.GetData(0, ref material); } catch { }
            }

            // Current selections from UI
            string selFamily = familyDrop != null
                ? GetSelected(familyDrop, familyToSections.Keys.FirstOrDefault() ?? "Unknown")
                : familyToSections.Keys.FirstOrDefault() ?? "Unknown";

            string selSection = sectionDrop != null
                ? GetSelected(sectionDrop, null)
                : null;

            if (string.IsNullOrEmpty(selSection))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No section is selected. Please choose a section from the library.");
                return;
            }

            // Keep the persisted selection state in sync with the UI
            storedFamily = selFamily;
            storedSection = selSection;

            // Read geometric data from the JSON DB and map to ISection parameters.
            double height, topWidth, topFlangeThickness, bottomWidth, bottomFlangeThickness, web;
            if (!TryGetSectionGeometry(selFamily, selSection, out height, out topWidth, out topFlangeThickness, out bottomWidth, out bottomFlangeThickness, out web))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Section '{selSection}' not found in the steel section database.");
                return;
            }

            var section = new Alpaca4d.Section.ISection(selSection, height, topWidth, topFlangeThickness, bottomWidth, bottomFlangeThickness, web, material);
            DA.SetData(0, section);
        }

        /// <summary>
        /// Tries to read the section geometry from the steel section database.
        /// The JSON is assumed to store dimensions in millimetres:
        ///   h  - overall depth
        ///   b  - flange width
        ///   tw - web thickness
        ///   tf - flange thickness
        /// These are converted to metres to be consistent with the rest of Alpaca4d.
        /// </summary>
        private bool TryGetSectionGeometry(string family, string sectionName,
            out double height, out double topWidth, out double topFlangeThickness,
            out double bottomWidth, out double bottomFlangeThickness, out double web)
        {
            height = topWidth = topFlangeThickness = bottomWidth = bottomFlangeThickness = web = 0.0;

            var db = LoadJsonResourceOnce(ref steelSectionDb, "Alpaca4d.Resources.Section.steel_section.json");
            if (db == null) return false;

            JObject sectionEntry = FindSectionEntry(db, family, sectionName);
            if (sectionEntry == null) return false;

            // Dimensions in mm in the database
            var h = TryGetDouble(sectionEntry, "h");
            var b = TryGetDouble(sectionEntry, "b");
            var tw = TryGetDouble(sectionEntry, "tw");
            var tf = TryGetDouble(sectionEntry, "tf");

            if (double.IsNaN(h) || double.IsNaN(b) || double.IsNaN(tw) || double.IsNaN(tf))
                return false;

            const double mmToM = 0.001;

            height = h * mmToM;
            topWidth = b * mmToM;
            bottomWidth = b * mmToM;
            topFlangeThickness = tf * mmToM;
            bottomFlangeThickness = tf * mmToM;
            web = tw * mmToM;

            return true;
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

        #endregion

        #region GH persistence

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            // Persist the last used selections so the UI can restore them.
            var chunk = writer.CreateChunk("SectionPresetSteel");
            if (!string.IsNullOrEmpty(storedFamily))
                chunk.SetString("Family", storedFamily);
            if (!string.IsNullOrEmpty(storedSection))
                chunk.SetString("Section", storedSection);

            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            // Load stored selections (if present) before the base logic triggers OnComponentLoaded.
            if (reader.ChunkExists("SectionPresetSteel"))
            {
                var chunk = reader.FindChunk("SectionPresetSteel");
                try { storedFamily = chunk.GetString("Family"); } catch { storedFamily = null; }
                try { storedSection = chunk.GetString("Section"); } catch { storedSection = null; }
            }

            return base.Read(reader);
        }

        #endregion
    }
}

