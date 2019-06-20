using System.Collections.Generic;
using UnityEngine;

namespace Common.Audio
{
	public abstract class AudioSettings : ScriptableObject
	{
		public abstract Dictionary<SystemLanguage, Dictionary<string, AudioClip>> Clips { get; }
	}
}