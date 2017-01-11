﻿using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace AssetBundleGraph {
	/**
		GUI Inspector to NodeGUI (Through NodeGUIInspectorHelper)
	*/
	[CustomEditor(typeof(NodeGUIInspectorHelper))]
	public class NodeGUIEditor : Editor {

		public static BuildTargetGroup currentEditingGroup = 
			BuildTargetUtility.DefaultTarget;

		[NonSerialized] private IModifier m_modifier;
		[NonSerialized] private IPrefabBuilder m_prefabBuilder;

		public override bool RequiresConstantRepaint() {
			return true;
		}

		private void DoInspectorLoaderGUI (NodeGUI node) {
			if (node.Data.LoaderLoadPath == null) {
				return;
			}

			EditorGUILayout.HelpBox("Loader: Load assets in given directory path.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = DrawOverrideTargetToggle(node, node.Data.LoaderLoadPath.ContainsValueOf(currentEditingGroup), (bool b) => {
					using(new RecordUndoScope("Remove Target Load Path Settings", node, true)) {
						if(b) {
							node.Data.LoaderLoadPath[currentEditingGroup] = node.Data.LoaderLoadPath.DefaultValue;
						} else {
							node.Data.LoaderLoadPath.Remove(currentEditingGroup);
						}
					}
				});

				using (disabledScope) {
					EditorGUILayout.LabelField("Load Path:");
					var newLoadPath = EditorGUILayout.TextField(
						SystemDataUtility.GetProjectName() + AssetBundleGraphSettings.ASSETS_PATH,
						node.Data.LoaderLoadPath[currentEditingGroup]
					);
					if (newLoadPath !=	node.Data.LoaderLoadPath[currentEditingGroup]) {
						using(new RecordUndoScope("Load Path Changed", node, true)){
							node.Data.LoaderLoadPath[currentEditingGroup] = newLoadPath;
						}
					}
				}
			}
		}

		private void DoInspectorFilterGUI (NodeGUI node) {
			EditorGUILayout.HelpBox("Filter: Filter incoming assets by keywords and types. You can use regular expressions for keyword field.", MessageType.Info);
			UpdateNodeName(node);

			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				GUILayout.Label("Filter Settings:");
				FilterEntry removing = null;
				for (int i= 0; i < node.Data.FilterConditions.Count; ++i) {
					var cond = node.Data.FilterConditions[i];

					Action messageAction = null;

					using (new GUILayout.HorizontalScope()) {
						if (GUILayout.Button("-", GUILayout.Width(30))) {
							removing = cond;
						}
						else {
							var newContainsKeyword = cond.FilterKeyword;

							GUIStyle s = new GUIStyle((GUIStyle)"TextFieldDropDownText");

							using (new EditorGUILayout.HorizontalScope()) {
								newContainsKeyword = EditorGUILayout.TextField(cond.FilterKeyword, s, GUILayout.Width(120));
								if (GUILayout.Button(cond.FilterKeytype , "Popup")) {
									var ind = i;// need this because of closure locality bug in unity C#
									NodeGUI.ShowFilterKeyTypeMenu(
										cond.FilterKeytype,
										(string selectedTypeStr) => {
											using(new RecordUndoScope("Modify Filter Type", node, true)){
												node.Data.FilterConditions[ind].FilterKeytype = selectedTypeStr;
											}
										} 
									);
								}
							}

							if (newContainsKeyword != cond.FilterKeyword) {
								using(new RecordUndoScope("Modify Filter Keyword", node, true)){
									cond.FilterKeyword = newContainsKeyword;
									// event must raise to propagate change to connection associated with point
									NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_CONNECTIONPOINT_LABELCHANGED, node, Vector2.zero, cond.ConnectionPoint));
								}
							}
						}
					}

					if(messageAction != null) {
						using (new GUILayout.HorizontalScope()) {
							messageAction.Invoke();
						}
					}
				}

				// add contains keyword interface.
				if (GUILayout.Button("+")) {
					using(new RecordUndoScope("Add Filter Condition", node)){
						node.Data.AddFilterCondition(
							AssetBundleGraphSettings.DEFAULT_FILTER_KEYWORD, 
							AssetBundleGraphSettings.DEFAULT_FILTER_KEYTYPE);
					}
				}

				if(removing != null) {
					using(new RecordUndoScope("Remove Filter Condition", node, true)){
						// event must raise to remove connection associated with point
						NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_CONNECTIONPOINT_DELETED, node, Vector2.zero, removing.ConnectionPoint));
						node.Data.RemoveFilterCondition(removing);
					}
				}
			}
		}

		private void DoInspectorImportSettingGUI (NodeGUI node) {
			EditorGUILayout.HelpBox("ImportSetting: Force apply import settings to given assets.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			/*
				importer node has no platform key. 
				platform key is contained by Unity's importer inspector itself.
			*/
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				Type incomingType = FindIncomingAssetType(node.Data.InputPoints[0]);
				IntegratedGUIImportSetting.ConfigStatus status = 
					IntegratedGUIImportSetting.GetConfigStatus(node.Data);

				if(incomingType == null) {
					// try to retrieve incoming type from configuration
					if(status == IntegratedGUIImportSetting.ConfigStatus.GoodSampleFound) {
						incomingType = IntegratedGUIImportSetting.GetReferenceAssetImporter(node.Data).GetType();
					} else {
						EditorGUILayout.HelpBox("ImportSetting needs a single type of incoming assets.", MessageType.Info);
						return;
					}
				}

				switch(status) {
				case IntegratedGUIImportSetting.ConfigStatus.NoSampleFound:
					// IntegratedGUIImportSetting.Setup() must run to grab another sample to configure.
					EditorGUILayout.HelpBox("Press Refresh to configure.", MessageType.Info);
					break;
				case IntegratedGUIImportSetting.ConfigStatus.GoodSampleFound:
					if (GUILayout.Button("Configure Import Setting")) {
						Selection.activeObject = IntegratedGUIImportSetting.GetReferenceAssetImporter(node.Data);
					}
					if (GUILayout.Button("Reset Import Setting")) {
						IntegratedGUIImportSetting.ResetConfig(node.Data);
					}
					break;
				case IntegratedGUIImportSetting.ConfigStatus.TooManySamplesFound:
					if (GUILayout.Button("Reset Import Setting")) {
						IntegratedGUIImportSetting.ResetConfig(node.Data);
					}
					break;
				}
			}
		}

		private void DoInspectorModifierGUI (NodeGUI node) {
			EditorGUILayout.HelpBox("Modifier: Modify asset settings.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {

				Type incomingType = FindIncomingAssetType(node.Data.InputPoints[0]);

				if(incomingType == null) {
					// if there is no asset input to determine incomingType,
					// retrieve from assigned Modifier.
					incomingType = ModifierUtility.GetModifierTargetType(node.Data.ScriptClassName);

					if(incomingType == null) {
						EditorGUILayout.HelpBox("Modifier needs a single type of incoming assets.", MessageType.Info);
						return;
					}
				}

				var map = ModifierUtility.GetAttributeClassNameMap(incomingType);
				if(map.Count > 0) {
					using(new GUILayout.HorizontalScope()) {
						GUILayout.Label("Modifier");
						var guiName = ModifierUtility.GetModifierGUIName(node.Data.ScriptClassName);
						if (GUILayout.Button(guiName, "Popup", GUILayout.MinWidth(150f))) {
							var builders = map.Keys.ToList();

							if(builders.Count > 0) {
								NodeGUI.ShowTypeNamesMenu(guiName, builders, (string selectedGUIName) => 
									{
										using(new RecordUndoScope("Change Modifier class", node, true)) {
											m_modifier = ModifierUtility.CreateModifier(selectedGUIName, incomingType);
											if(m_modifier != null) {
												node.Data.ScriptClassName = ModifierUtility.GUINameToClassName(selectedGUIName, incomingType);
												node.Data.InstanceData[currentEditingGroup] = m_modifier.Serialize();
											}
										}
									}  
								);
							}
						}
					}

				} else {
					string[] menuNames = AssetBundleGraphSettings.GUI_TEXT_MENU_GENERATE_MODIFIER.Split('/');
					EditorGUILayout.HelpBox(
						string.Format(
							"No CustomModifier found for {3} type. \n" +
							"You need to create at least one Modifier script to select script for Modifier. " +
							"To start, select {0}>{1}>{2} menu and create a new script.",
							menuNames[1],menuNames[2], menuNames[3], incomingType.FullName
						), MessageType.Info);
				}

				GUILayout.Space(10f);

				if(DrawPlatformSelector(node)) {
					// if platform tab is changed, renew modifierModifierInstance for that tab.
					m_modifier = null;
				}
				using (new EditorGUILayout.VerticalScope()) {
					var disabledScope = DrawOverrideTargetToggle(node, node.Data.InstanceData.ContainsValueOf(currentEditingGroup), (bool enabled) => {
						if(enabled) {
							node.Data.InstanceData[currentEditingGroup] = node.Data.InstanceData.DefaultValue;
						} else {
							node.Data.InstanceData.Remove(currentEditingGroup);
						}
						m_modifier = null;						
					});

					using (disabledScope) {
						//reload modifierModifier instance from saved modifierModifier data.
						if (m_modifier == null) {
							m_modifier = ModifierUtility.CreateModifier(node.Data, currentEditingGroup);
							if(m_modifier != null) {
								node.Data.ScriptClassName = m_modifier.GetType().FullName;
								if(node.Data.InstanceData.ContainsValueOf(currentEditingGroup)) {
									node.Data.InstanceData[currentEditingGroup] = m_modifier.Serialize();
								}
							}
						}

						if (m_modifier != null) {
							Action onChangedAction = () => {
								using(new RecordUndoScope("Change Modifier Setting", node)) {
									node.Data.ScriptClassName = m_modifier.GetType().FullName;
									if(node.Data.InstanceData.ContainsValueOf(currentEditingGroup)) {
										node.Data.InstanceData[currentEditingGroup] = m_modifier.Serialize();
									}
								}
							};

							m_modifier.OnInspectorGUI(onChangedAction);
						}
					}
				}
			}
		}

		private void DoInspectorGroupingGUI (NodeGUI node) {
			if (node.Data.GroupingKeywords == null) {
				return;
			}

			EditorGUILayout.HelpBox("Grouping: Create group of assets.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = DrawOverrideTargetToggle(node, node.Data.GroupingKeywords.ContainsValueOf(currentEditingGroup), (bool enabled) => {
					using(new RecordUndoScope("Remove Target Grouping Keyword Settings", node, true)){
						if(enabled) {
							node.Data.GroupingKeywords[currentEditingGroup] = node.Data.GroupingKeywords.DefaultValue;
						} else {
							node.Data.GroupingKeywords.Remove(currentEditingGroup);
						}
					}
				});

				using (disabledScope) {
					var newGroupingKeyword = EditorGUILayout.TextField("Grouping Keyword",node.Data.GroupingKeywords[currentEditingGroup]);
					EditorGUILayout.HelpBox(
						"Grouping Keyword requires \"*\" in itself. It assumes there is a pattern such as \"ID_0\" in incoming paths when configured as \"ID_*\" ", 
						MessageType.Info);

					if (newGroupingKeyword != node.Data.GroupingKeywords[currentEditingGroup]) {
						using(new RecordUndoScope("Change Grouping Keywords", node, true)){
							node.Data.GroupingKeywords[currentEditingGroup] = newGroupingKeyword;
						}
					}
				}
			}
		}

		private void DoInspectorPrefabBuilderGUI (NodeGUI node) {
			EditorGUILayout.HelpBox("PrefabBuilder: Create prefab with given assets and script.", MessageType.Info);
			UpdateNodeName(node);

			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {

				var map = PrefabBuilderUtility.GetAttributeClassNameMap();
				if(map.Count > 0) {
					using(new GUILayout.HorizontalScope()) {
						GUILayout.Label("PrefabBuilder");
						var guiName = PrefabBuilderUtility.GetPrefabBuilderGUIName(node.Data.ScriptClassName);

						if (GUILayout.Button(guiName, "Popup", GUILayout.MinWidth(150f))) {
							var builders = map.Keys.ToList();

							if(builders.Count > 0) {
								NodeGUI.ShowTypeNamesMenu(guiName, builders, (string selectedGUIName) => 
									{
										using(new RecordUndoScope("Change PrefabBuilder class", node, true)) {
											m_prefabBuilder = PrefabBuilderUtility.CreatePrefabBuilder(selectedGUIName);
											if(m_prefabBuilder != null) {
												node.Data.ScriptClassName = PrefabBuilderUtility.GUINameToClassName(selectedGUIName);
												node.Data.InstanceData.DefaultValue = m_prefabBuilder.Serialize();
											}
										}
									} 
								);
							}
						}
					}
				} else {
					if(!string.IsNullOrEmpty(node.Data.ScriptClassName)) {
						EditorGUILayout.HelpBox(
							string.Format(
								"Your PrefabBuilder script {0} is missing from assembly. Did you delete script?", node.Data.ScriptClassName), MessageType.Info);
					} else {
						string[] menuNames = AssetBundleGraphSettings.GUI_TEXT_MENU_GENERATE_PREFABBUILDER.Split('/');
						EditorGUILayout.HelpBox(
							string.Format(
								"You need to create at least one PrefabBuilder script to use PrefabBuilder node. To start, select {0}>{1}>{2} menu and create new script from template.",
								menuNames[1],menuNames[2], menuNames[3]
							), MessageType.Info);
					}
				}

				GUILayout.Space(10f);

				if(DrawPlatformSelector(node)) {
					// if platform tab is changed, renew prefabBuilder for that tab.
					m_prefabBuilder = null;
				}
				using (new EditorGUILayout.VerticalScope()) {
					var disabledScope = DrawOverrideTargetToggle(node, node.Data.InstanceData.ContainsValueOf(currentEditingGroup), (bool enabled) => {
						if(enabled) {
							node.Data.InstanceData[currentEditingGroup] = node.Data.InstanceData.DefaultValue;
						} else {
							node.Data.InstanceData.Remove(currentEditingGroup);
						}
						m_prefabBuilder = null;
					});

					using (disabledScope) {
						//reload prefabBuilder instance from saved instance data.
						if (m_prefabBuilder == null) {
							m_prefabBuilder = PrefabBuilderUtility.CreatePrefabBuilder(node.Data, currentEditingGroup);
							if(m_prefabBuilder != null) {
								node.Data.ScriptClassName = m_prefabBuilder.GetType().FullName;
								if(node.Data.InstanceData.ContainsValueOf(currentEditingGroup)) {
									node.Data.InstanceData[currentEditingGroup] = m_prefabBuilder.Serialize();
								}
							}
						}

						if (m_prefabBuilder != null) {
							Action onChangedAction = () => {
								using(new RecordUndoScope("Change PrefabBuilder Setting", node)) {
									node.Data.ScriptClassName = m_prefabBuilder.GetType().FullName;
									if(node.Data.InstanceData.ContainsValueOf(currentEditingGroup)) {
										node.Data.InstanceData[currentEditingGroup] = m_prefabBuilder.Serialize();
									}
								}
							};

							m_prefabBuilder.OnInspectorGUI(onChangedAction);
						}
					}
				}
			}
		}
		
		private void DoInspectorBundleConfiguratorGUI (NodeGUI node) {
			if (node.Data.BundleNameTemplate == null) return;

			EditorGUILayout.HelpBox("BundleConfigurator: Create asset bundle settings with given group of assets.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = DrawOverrideTargetToggle(node, node.Data.BundleNameTemplate.ContainsValueOf(currentEditingGroup), (bool enabled) => {
					using(new RecordUndoScope("Remove Target Bundle Name Template Setting", node, true)){
						if(enabled) {
							node.Data.BundleNameTemplate[currentEditingGroup] = node.Data.BundleNameTemplate.DefaultValue;
						} else {
							node.Data.BundleNameTemplate.Remove(currentEditingGroup);
						}
					}
				});

				using (disabledScope) {
					var bundleNameTemplate = EditorGUILayout.TextField("Bundle Name Template", node.Data.BundleNameTemplate[currentEditingGroup]).ToLower();

					if (bundleNameTemplate != node.Data.BundleNameTemplate[currentEditingGroup]) {
						using(new RecordUndoScope("Change Bundle Name Template", node, true)){
							node.Data.BundleNameTemplate[currentEditingGroup] = bundleNameTemplate;
						}
					}
				}
			}

			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				GUILayout.Label("Variants:");
				var variantNames = node.Data.Variants.Select(v => v.Name).ToList();
				Variant removing = null;
				foreach (var v in node.Data.Variants) {
					using (new GUILayout.HorizontalScope()) {
						if (GUILayout.Button("-", GUILayout.Width(30))) {
							removing = v;
						}
						else {
							GUIStyle s = new GUIStyle((GUIStyle)"TextFieldDropDownText");
							Action makeStyleBold = () => {
								s.fontStyle = FontStyle.Bold;
								s.fontSize = 12;
							};

							IntegratedGUIBundleConfigurator.ValidateVariantName(v.Name, variantNames, 
								makeStyleBold,
								makeStyleBold,
								makeStyleBold);

							var variantName = EditorGUILayout.TextField(v.Name, s);

							if (variantName != v.Name) {
								using(new RecordUndoScope("Change Variant Name", node, true)){
									v.Name = variantName;
								}
							}
						}
					}
				}
				if (GUILayout.Button("+")) {
					using(new RecordUndoScope("Add Variant", node, true)){
						node.Data.AddVariant(AssetBundleGraphSettings.BUNDLECONFIG_VARIANTNAME_DEFAULT);
					}
				}
				if(removing != null) {
					using(new RecordUndoScope("Remove Variant", node, true)){
						// event must raise to remove connection associated with point
						NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_CONNECTIONPOINT_DELETED, node, Vector2.zero, removing.ConnectionPoint));
						node.Data.RemoveVariant(removing);
					}
				}
			}
		}

		private void DoInspectorBundleBuilderGUI (NodeGUI node) {
			if (node.Data.BundleBuilderBundleOptions == null) {
				return;
			}

			EditorGUILayout.HelpBox("BundleBuilder: Build asset bundles with given asset bundle settings.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = DrawOverrideTargetToggle(node, node.Data.BundleBuilderBundleOptions.ContainsValueOf(currentEditingGroup), (bool enabled) => {
					using(new RecordUndoScope("Remove Target Bundle Options", node, true)){
						if(enabled) {
							node.Data.BundleBuilderBundleOptions[currentEditingGroup] = node.Data.BundleBuilderBundleOptions.DefaultValue;
						}  else {
							node.Data.BundleBuilderBundleOptions.Remove(currentEditingGroup);
						}
					}
				} );

				using (disabledScope) {
					int bundleOptions = node.Data.BundleBuilderBundleOptions[currentEditingGroup];

					bool isDisableWriteTypeTreeEnabled  = 0 < (bundleOptions & (int)BuildAssetBundleOptions.DisableWriteTypeTree);
					bool isIgnoreTypeTreeChangesEnabled = 0 < (bundleOptions & (int)BuildAssetBundleOptions.IgnoreTypeTreeChanges);

					// buildOptions are validated during loading. Two flags should not be true at the same time.
					UnityEngine.Assertions.Assert.IsFalse(isDisableWriteTypeTreeEnabled && isIgnoreTypeTreeChangesEnabled);

					bool isSomethingDisabled = isDisableWriteTypeTreeEnabled || isIgnoreTypeTreeChangesEnabled;

					foreach (var option in AssetBundleGraphSettings.BundleOptionSettings) {

						// contains keyword == enabled. if not, disabled.
						bool isEnabled = (bundleOptions & (int)option.option) != 0;

						bool isToggleDisabled = 
							(option.option == BuildAssetBundleOptions.DisableWriteTypeTree  && isIgnoreTypeTreeChangesEnabled) ||
							(option.option == BuildAssetBundleOptions.IgnoreTypeTreeChanges && isDisableWriteTypeTreeEnabled);

						using(new EditorGUI.DisabledScope(isToggleDisabled)) {
							var result = EditorGUILayout.ToggleLeft(option.description, isEnabled);
							if (result != isEnabled) {
								using(new RecordUndoScope("Change Bundle Options", node, true)){
									bundleOptions = (result) ? 
										((int)option.option | bundleOptions) : 
										(((~(int)option.option)) & bundleOptions);
									node.Data.BundleBuilderBundleOptions[currentEditingGroup] = bundleOptions;
								}
							}
						}
					}
					if(isSomethingDisabled) {
						EditorGUILayout.HelpBox("'Disable Write Type Tree' and 'Ignore Type Tree Changes' can not be used together.", MessageType.Info);
					}
				}
			}
		}


		private void DoInspectorExporterGUI (NodeGUI node) {
			if (node.Data.ExporterExportPath == null) {
				return;
			}

			EditorGUILayout.HelpBox("Exporter: Export given files to output directory.", MessageType.Info);
			UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = DrawOverrideTargetToggle(node, node.Data.ExporterExportPath.ContainsValueOf(currentEditingGroup), (bool enabled) => {
					using(new RecordUndoScope("Remove Target Export Settings", node, true)){
						if(enabled) {
							node.Data.ExporterExportPath[currentEditingGroup] = node.Data.ExporterExportPath.DefaultValue;
						}  else {
							node.Data.ExporterExportPath.Remove(currentEditingGroup);
						}
					}
				} );

				using (disabledScope) {
					EditorGUILayout.LabelField("Export Path:");
					var newExportPath = EditorGUILayout.TextField(
						SystemDataUtility.GetProjectName(), 
						node.Data.ExporterExportPath[currentEditingGroup]
					);

					var exporterrNodePath = FileUtility.GetPathWithProjectPath(newExportPath);
					if(IntegratedGUIExporter.ValidateExportPath(
						newExportPath,
						exporterrNodePath,
						() => {
							// TODO Make text field bold
						},
						() => {
							using (new EditorGUILayout.HorizontalScope()) {
								EditorGUILayout.LabelField(exporterrNodePath + " does not exist.");
								if(GUILayout.Button("Create directory")) {
									using(new SaveScope(node)) {
										Directory.CreateDirectory(exporterrNodePath);
									}
								}
							}
							EditorGUILayout.Space();

							EditorGUILayout.LabelField("Available Directories:");
							string[] dirs = Directory.GetDirectories(Path.GetDirectoryName(exporterrNodePath));
							foreach(string s in dirs) {
								EditorGUILayout.LabelField(s);
							}
						}
					)) {
						using (new EditorGUILayout.HorizontalScope()) {
							GUILayout.FlexibleSpace();
							#if UNITY_EDITOR_OSX
							string buttonName = "Reveal in Finder";
							#else
							string buttonName = "Show in Explorer";
							#endif 
							if(GUILayout.Button(buttonName)) {
								EditorUtility.RevealInFinder(exporterrNodePath);
							}
						}
					}
						
					if (newExportPath != node.Data.ExporterExportPath[currentEditingGroup]) {
						using(new RecordUndoScope("Change Export Path", node, true)){
							node.Data.ExporterExportPath[currentEditingGroup] = newExportPath;
						}
					}
				}
			}
		}


		public override void OnInspectorGUI () {
			var currentTarget = (NodeGUIInspectorHelper)target;
			var node = currentTarget.node;
			if (node == null) return;

			switch (node.Kind) {
			case NodeKind.LOADER_GUI:
				DoInspectorLoaderGUI(node);
				break;
			case NodeKind.FILTER_GUI:
				DoInspectorFilterGUI(node);
				break;
			case NodeKind.IMPORTSETTING_GUI :
				DoInspectorImportSettingGUI(node);
				break;
			case NodeKind.MODIFIER_GUI :
				DoInspectorModifierGUI(node);
				break;
			case NodeKind.GROUPING_GUI:
				DoInspectorGroupingGUI(node);
				break;
			case NodeKind.PREFABBUILDER_GUI:
				DoInspectorPrefabBuilderGUI(node);
				break;
			case NodeKind.BUNDLECONFIG_GUI:
				DoInspectorBundleConfiguratorGUI(node);
				break;
			case NodeKind.BUNDLEBUILDER_GUI:
				DoInspectorBundleBuilderGUI(node);
				break;
			case NodeKind.EXPORTER_GUI: 
				DoInspectorExporterGUI(node);
				break;
			default: 
				Debug.LogError(node.Name + " is defined as unknown kind of node. value:" + node.Kind);
				break;
			}

			var errors = currentTarget.errors;
			if (errors != null && errors.Any()) {
				foreach (var error in errors) {
					EditorGUILayout.HelpBox(error, MessageType.Error);
				}
			}
		}

		private void ShowFilterKeyTypeMenu (string current, Action<string> ExistSelected) {
			var menu = new GenericMenu();
			
			menu.AddDisabledItem(new GUIContent(current));
			
			menu.AddSeparator(string.Empty);
			
			for (var i = 0; i < TypeUtility.KeyTypes.Count; i++) {
				var type = TypeUtility.KeyTypes[i];
				if (type == current) continue;
				
				menu.AddItem(
					new GUIContent(type),
					false,
					() => {
						ExistSelected(type);
					}
				);
			}
			menu.ShowAsContext();
		}

		private Type FindIncomingAssetType(ConnectionPointData inputPoint) {
			var assetGroups = AssetBundleGraphEditorWindow.GetIncomingAssetGroups(inputPoint);
			if(assetGroups == null) {
				return null;
			}
			return TypeUtility.FindIncomingAssetType(assetGroups.SelectMany(v => v.Value).ToList());
		}

		private void UpdateNodeName (NodeGUI node) {
			var newName = EditorGUILayout.TextField("Node Name", node.Name);

			if( NodeGUIUtility.allNodeNames != null ) {
				var overlapping = NodeGUIUtility.allNodeNames.GroupBy(x => x)
					.Where(group => group.Count() > 1)
					.Select(group => group.Key);
				if (overlapping.Any() && overlapping.Contains(newName)) {
					EditorGUILayout.HelpBox("This node name already exist. Please put other name:" + newName, MessageType.Error);
					AssetBundleGraphEditorWindow.AddNodeException(new NodeException("Node name " + newName + " already exist.", node.Id ));
				}
			}

			if (newName != node.Name) {
				using(new RecordUndoScope("Change Node Name", node, true)){
					node.Name = newName;
				}
			}
		}

		/*
		 *  Return true if Platform is changed
		 */ 
		private bool DrawPlatformSelector (NodeGUI node) {
			BuildTargetGroup g = currentEditingGroup;
			bool editGroupChanged = false;

			EditorGUI.BeginChangeCheck();
			using (new EditorGUILayout.HorizontalScope()) {
				var choosenIndex = -1;
				for (var i = 0; i < NodeGUIUtility.platformButtons.Length; i++) {
					var onOffBefore = NodeGUIUtility.platformButtons[i].targetGroup == currentEditingGroup;
					var onOffAfter = onOffBefore;

					GUIStyle toolbarbutton = new GUIStyle("toolbarbutton");

					if(NodeGUIUtility.platformButtons[i].targetGroup == BuildTargetUtility.DefaultTarget) {
						onOffAfter = GUILayout.Toggle(onOffBefore, NodeGUIUtility.platformButtons[i].ui, toolbarbutton);
					} else {
						var width = Mathf.Max(32f, toolbarbutton.CalcSize(NodeGUIUtility.platformButtons[i].ui).x);
						onOffAfter = GUILayout.Toggle(onOffBefore, NodeGUIUtility.platformButtons[i].ui, toolbarbutton, GUILayout.Width( width ));
					}

					if (onOffBefore != onOffAfter) {
						choosenIndex = i;
						break;
					}
				}

				if (EditorGUI.EndChangeCheck()) {
					g = NodeGUIUtility.platformButtons[choosenIndex].targetGroup;
				}
			}

			if (g != currentEditingGroup) {
				currentEditingGroup = g;
				editGroupChanged = true;
				GUI.FocusControl(string.Empty);
			}

			return editGroupChanged;
		}

		private EditorGUI.DisabledScope DrawOverrideTargetToggle(NodeGUI node, bool status, Action<bool> onStatusChange) {

			if( currentEditingGroup == BuildTargetUtility.DefaultTarget ) {
				return new EditorGUI.DisabledScope(false);
			}

			bool newStatus = GUILayout.Toggle(status, 
				"Override for " + NodeGUIUtility.GetPlatformButtonFor(currentEditingGroup).ui.tooltip);
			
			if(newStatus != status && onStatusChange != null) {
				onStatusChange(newStatus);
			}
			return new EditorGUI.DisabledScope(!newStatus);
		}
	}
}