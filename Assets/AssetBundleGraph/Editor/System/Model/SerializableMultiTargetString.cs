using UnityEngine;
using UnityEditor;

using System;
using System.Linq;
using System.Collections.Generic;

namespace AssetBundleGraph {

	[Serializable] 
	public class SerializableMultiTargetString {

		[Serializable]
		public class Entry {
			[SerializeField] public BuildTargetGroup targetGroup;
			[SerializeField] public string value;

			public Entry(BuildTargetGroup g, string v) {
				targetGroup = g;
				value = v;
			}
		}

		[SerializeField] private List<Entry> m_values;

		public SerializableMultiTargetString(string value) {
			m_values = new List<Entry>();
			this[BuildTargetUtility.DefaultTarget] = value;
		}

		public SerializableMultiTargetString() {
			m_values = new List<Entry>();
		}

		public SerializableMultiTargetString(SerializableMultiTargetString rhs) {
			m_values = new List<Entry>();
			foreach(var v in rhs.m_values) {
				m_values.Add(new Entry(v.targetGroup, v.value));
			}
		}

		public SerializableMultiTargetString(Dictionary<string, object> json) {
			m_values = new List<Entry>();
			foreach (var buildTargetName in json.Keys) {
				try {
					BuildTargetGroup g =  (BuildTargetGroup)Enum.Parse(typeof(BuildTargetGroup), buildTargetName, true);
					string val = json[buildTargetName] as string;
					m_values.Add(new Entry(g, val));
				} catch(Exception e) {
					Debug.LogWarning("Failed to retrieve SerializableMultiTargetString. skipping entry - " + buildTargetName + ":" + json[buildTargetName] + " error:" + e.Message);
				}
			}
		}


		public List<Entry> Values {
			get {
				return m_values;
			}
		}

		public string this[BuildTargetGroup g] {
			get {
				int i = m_values.FindIndex(v => v.targetGroup == g);
				if(i >= 0) {
					return m_values[i].value;
				} else {
					return DefaultValue;
				}
			}
			set {
				int i = m_values.FindIndex(v => v.targetGroup == g);
				if(i >= 0) {
					m_values[i].value = value;
				} else {
					m_values.Add(new Entry(g, value));
				}
			}
		}

		public string this[BuildTarget index] {
			get {
				return this[BuildTargetUtility.TargetToGroup(index)];
			}
			set {
				this[BuildTargetUtility.TargetToGroup(index)] = value;
			}
		}

		public string DefaultValue {
			get {
				int i = m_values.FindIndex(v => v.targetGroup == BuildTargetUtility.DefaultTarget);
				if(i >= 0) {
					return m_values[i].value;
				} else {
					string defaultValue = string.Empty;
					m_values.Add(new Entry(BuildTargetUtility.DefaultTarget, defaultValue));
					return defaultValue;
				}
			}
			set {
				this[BuildTargetUtility.DefaultTarget] = value;
			}
		}

		public string CurrentPlatformValue {
			get {
				return this[EditorUserBuildSettings.selectedBuildTargetGroup];
			}
		}

		public bool ContainsValueOf (BuildTargetGroup group) {
			return m_values.FindIndex(v => v.targetGroup == group) >= 0;
		}

		public void Remove (BuildTargetGroup group) {
			int index = m_values.FindIndex(v => v.targetGroup == group);
			if(index >= 0) {
				m_values.RemoveAt(index);
			}
		}

		public Dictionary<string, object> ToJsonDictionary() {
			Dictionary<string, object> dic = new Dictionary<string, object>();
			foreach(Entry e in m_values) {
				dic.Add(e.targetGroup.ToString(), e.value);
			}
			return dic;
		}
	}
}