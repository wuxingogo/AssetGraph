using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace AssetBundleGraph {
	public class IntegratedGUIExporter : INodeOperation {
		public void Setup (BuildTarget target, 
			NodeData node, 
			ConnectionPointData inputPoint,
			ConnectionData connectionToOutput, 
			Dictionary<string, List<Asset>> inputGroupAssets, 
			List<string> alreadyCached, 
			Action<ConnectionData, Dictionary<string, List<Asset>>, List<string>> Output) 
		{
			ValidateExportPath(
				node.ExporterExportPath[target],
				node.ExporterExportPath[target],
				() => {
					throw new NodeException(node.Name + ":Export Path is empty.", node.Id);
				},
				() => {
					throw new NodeException(node.Name + ":Directory set to Export Path does not exist. Path:" + node.ExporterExportPath[target], node.Id);
				}
			);

			Export(target, node, inputPoint, connectionToOutput, inputGroupAssets, Output, false);
		}
		
		public void Run (BuildTarget target, 
			NodeData node, 
			ConnectionPointData inputPoint,
			ConnectionData connectionToOutput, 
			Dictionary<string, List<Asset>> inputGroupAssets, 
			List<string> alreadyCached, 
			Action<ConnectionData, Dictionary<string, List<Asset>>, List<string>> Output) 
		{
			Export(target, node, inputPoint, connectionToOutput, inputGroupAssets, Output, true);
		}

		private void Export (BuildTarget target, 
			NodeData node, 
			ConnectionPointData inputPoint,
			ConnectionData connectionToOutput, 
			Dictionary<string, List<Asset>> inputGroupAssets, 
			Action<ConnectionData, Dictionary<string, List<Asset>>, List<string>> Output,
			bool isRun) 
		{
			var outputDict = new Dictionary<string, List<Asset>>();
			outputDict["0"] = new List<Asset>();

			var failedExports = new List<string>();

			foreach (var groupKey in inputGroupAssets.Keys) {
				var exportedAssets = new List<Asset>();
				var inputSources = inputGroupAssets[groupKey];

				foreach (var source in inputSources) {
					if (isRun) {
						if (!Directory.Exists(node.ExporterExportPath[target])) {
							Directory.CreateDirectory(node.ExporterExportPath[target]);
						}
					}
					
					var destinationSourcePath = source.importFrom;
					
					// in bundleBulider, use platform-package folder for export destination.
					if (destinationSourcePath.StartsWith(AssetBundleGraphSettings.BUNDLEBUILDER_CACHE_PLACE)) {
						var depth = AssetBundleGraphSettings.BUNDLEBUILDER_CACHE_PLACE.Split(AssetBundleGraphSettings.UNITY_FOLDER_SEPARATOR).Length + 1;
						
						var splitted = destinationSourcePath.Split(AssetBundleGraphSettings.UNITY_FOLDER_SEPARATOR);
						var reducedArray = new string[splitted.Length - depth];
						
						Array.Copy(splitted, depth, reducedArray, 0, reducedArray.Length);
						var fromDepthToEnd = string.Join(AssetBundleGraphSettings.UNITY_FOLDER_SEPARATOR.ToString(), reducedArray);
						
						destinationSourcePath = fromDepthToEnd;
					}
					
					var destination = FileUtility.PathCombine(node.ExporterExportPath[target], destinationSourcePath);
					
					var parentDir = Directory.GetParent(destination).ToString();

					if (isRun) {
						if (!Directory.Exists(parentDir)) {
							Directory.CreateDirectory(parentDir);
						}
						if (File.Exists(destination)) {
							File.Delete(destination);
						}
						if (string.IsNullOrEmpty(source.importFrom)) {
							failedExports.Add(source.absoluteAssetPath);
							continue;
						}
						try {
							File.Copy(source.importFrom, destination);
						} catch(Exception e) {
							failedExports.Add(source.importFrom);
							Debug.LogError(node.Name + ": Error occured: " + e.Message);
						}
					}

					var exportedAsset = Asset.CreateAssetWithExportPath(destination);
					exportedAssets.Add(exportedAsset);
				}
				outputDict["0"].AddRange(exportedAssets);
			}

			if (failedExports.Any()) {
				Debug.LogError(node.Name + ": Failed to export files. All files must be imported before exporting: " + string.Join(", ", failedExports.ToArray()));
			}

			Output(connectionToOutput, outputDict, null);
		}

		public static bool ValidateExportPath (string currentExportFilePath, string combinedPath, Action NullOrEmpty, Action DoesNotExist) {
			if (string.IsNullOrEmpty(currentExportFilePath)) {
				NullOrEmpty();
				return false;
			}
			if (!Directory.Exists(combinedPath)) {
				DoesNotExist();
				return false;
			}
			return true;
		}
	}
}