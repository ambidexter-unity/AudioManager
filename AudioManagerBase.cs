#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

// ReSharper disable once CheckNamespace
namespace Common.Audio
{
	[Serializable]
	public enum Language
	{
		Afrikaans = 0,
		Arabic = 1,
		Basque = 2,
		Belarusian = 3,
		Bulgarian = 4,
		Catalan = 5,
		Chinese = 6,
		Czech = 7,
		Danish = 8,
		Dutch = 9,
		English = 10,
		Estonian = 11,
		Faroese = 12,
		Finnish = 13,
		French = 14,
		German = 15,
		Greek = 16,
		Hebrew = 17,
		Hungarian = 18,
		Icelandic = 19,
		Indonesian = 20,
		Italian = 21,
		Japanese = 22,
		Korean = 23,
		Latvian = 24,
		Lithuanian = 25,
		Norwegian = 26,
		Polish = 27,
		Portuguese = 28,
		Romanian = 29,
		Russian = 30,
		Serbocroatian = 31,
		Slovak = 32,
		Slovenian = 33,
		Spanish = 34,
		Swedish = 35,
		Thai = 36,
		Turkish = 37,
		Ukrainian = 38,
		Vietnamese = 39,
		Chinesesimplified = 40,
		Chinesetraditional = 41,
		Unknown = 42
	}

	[Serializable]
	public class AudioResourceRecord
	{
		// ReSharper disable InconsistentNaming
		public string Id;

		public AudioClip Clip;
		// ReSharper restore InconsistentNaming
	}

	[Serializable]
	public class AudioLocal
	{
		// ReSharper disable InconsistentNaming
		public Language Language = Language.Unknown;

		public AudioResourceRecord[] Clips = new AudioResourceRecord[0];
		// ReSharper restore InconsistentNaming
	}

	public abstract class AudioManagerBase : MonoBehaviour, IAudioManager
	{
		[Serializable]
		private struct PersistentData
		{
			public bool _muteSound;
			public bool _muteMusic;
			public float _soundVolume;
			public float _musicVolume;
		}

		// Вспомогательный класс для хранения текущего воспроизводимого клипа.
		private class SoundItem : IComparable<SoundItem>
		{
			private readonly int _priority;

			public SoundItem(AudioSource audioSource, int soundId, int priority, bool exclusive)
			{
				AudioSource = audioSource;
				SoundId = soundId;
				Exclusive = exclusive;
				_priority = priority;
			}

			public AudioSource AudioSource { get; }

			public int SoundId { get; }

			public bool Exclusive { get; }

			public int CompareTo(SoundItem other)
			{
				if (other._priority < _priority) return -1;
				if (other._priority > _priority) return 1;
				if (other.SoundId < SoundId) return -1;
				if (other.SoundId > SoundId) return 1;
				return 0;
			}
		}
		//---------------------------------------//


		private bool _isInitialized;
		private static int _currentId;

		// ReSharper disable once InconsistentNaming
		private const int SoundsLimit = 8;

		private const float MuffleMinValue = 0.1f;
		private const float MuffleMaxValue = 0.99f;

		private float _musicVolume = 1f;
		private float _soundVolume = 1f;

		private AudioSource _musicSource;
		private AudioSource _musicOldSource;

		private string _lastMusicId = string.Empty;

		private bool _muteMusic;
		private bool _muteSound;

		private readonly List<AudioSource> _sndObjectPool = new List<AudioSource>(SoundsLimit);

		private readonly Dictionary<SystemLanguage, Dictionary<string, AudioClip>> _registeredClips =
			new Dictionary<SystemLanguage, Dictionary<string, AudioClip>>();

		private readonly SortedDictionary<SoundItem, Coroutine> _sounds =
			new SortedDictionary<SoundItem, Coroutine>();

		private int _muffleSoundId;
		private float _mufflePercent;

		private readonly Dictionary<AudioSource, int> _externalAudioSources = new Dictionary<AudioSource, int>();

		private readonly Dictionary<AudioSource, Coroutine> _fadeRoutines = new Dictionary<AudioSource, Coroutine>();

#pragma warning disable 649
		[Header("Global clips"), SerializeField]
		private AudioLocal[] _locales = new AudioLocal[0];
#pragma warning restore 649

		private void Awake()
		{
			Init();
		}

		private void OnDestroy()
		{
			foreach (var coroutine in _fadeRoutines.Values)
			{
				StopCoroutine(coroutine);
			}

			foreach (var coroutine in _sounds.Values)
			{
				if (coroutine == null) continue;
				StopCoroutine(coroutine);
			}

			_fadeRoutines.Clear();
		}

		protected abstract string AudioPersistKey { get; }

		protected virtual void Init()
		{
			if (_isInitialized) return;
			_isInitialized = true;

			RestorePersistingState();
			foreach (var locale in _locales)
			{
				if (Enum.IsDefined(typeof(SystemLanguage), (int) locale.Language))
				{
					var lang = (SystemLanguage) (int) locale.Language;
					RegisterClips(locale.Clips.ToDictionary(record => record.Id, record => record.Clip), lang);
				}
				else
				{
					Debug.LogErrorFormat("Can't resolve {0} Language enum value.",
						typeof(Language).GetEnumName(locale.Language));
				}
			}
		}

		public float MusicVolume
		{
			get => _musicVolume;
			private set
			{
				if (value.Equals(_musicVolume)) return;
				var oldValue = _musicVolume;
				_musicVolume = value;
				MusicVolumeChangedEvent?.Invoke(_musicVolume, oldValue);
			}
		}

		public float SoundVolume
		{
			get => _soundVolume;
			private set
			{
				if (value.Equals(_soundVolume)) return;
				var oldValue = _soundVolume;
				_soundVolume = value;
				SoundVolumeChangedEvent?.Invoke(_soundVolume, oldValue);
			}
		}

		public event VolumeChangedHandler MusicVolumeChangedEvent;

		public event VolumeChangedHandler SoundVolumeChangedEvent;

		private void PersistCurrentState()
		{
			var data = JsonUtility.ToJson(new PersistentData
			{
				_muteMusic = MuteMusic,
				_muteSound = MuteSound,
				_musicVolume = MusicVolume,
				_soundVolume = SoundVolume
			});
			PlayerPrefs.SetString(AudioPersistKey, data);
			PlayerPrefs.Save();
		}

		private void RestorePersistingState()
		{
			if (!PlayerPrefs.HasKey(AudioPersistKey)) return;
			var data = JsonUtility.FromJson<PersistentData>(PlayerPrefs.GetString(AudioPersistKey));
			MusicVolume = data._musicVolume;
			SoundVolume = data._soundVolume;
			MuteMusic = data._muteMusic;
			MuteSound = data._muteSound;
		}

		// IAudioManager

		public void RegisterClips(Dictionary<string, AudioClip> clips, SystemLanguage language = SystemLanguage.Unknown)
		{
			if (!_registeredClips.TryGetValue(language, out var locale))
			{
				locale = new Dictionary<string, AudioClip>();
				_registeredClips.Add(language, locale);
			}

			foreach (var pair in clips)
			{
				if (locale.ContainsKey(pair.Key))
				{
					Debug.LogWarningFormat("Clip with the key {0} already registered in AudioManager.", pair.Key);
					continue;
				}

				locale.Add(pair.Key, pair.Value);
			}
		}

		public void UnregisterClips(IEnumerable<string> clipIds)
		{
			var ids = clipIds.ToArray();
			foreach (var locale in _registeredClips)
			{
				foreach (var clipId in ids)
				{
					if (!locale.Value.TryGetValue(clipId, out var clip)) continue;
					locale.Value.Remove(clipId);

					if (_musicSource != null && _musicSource.clip == clip)
					{
						KillFadeCoroutine(_musicSource);
						Destroy(_musicSource.gameObject);
						_musicSource = null;
						_lastMusicId = string.Empty;
						continue;
					}

					if (_musicOldSource != null && _musicOldSource.clip == clip)
					{
						KillFadeCoroutine(_musicOldSource);
						Destroy(_musicOldSource.gameObject);
						_musicOldSource = null;
						continue;
					}

					_sndObjectPool.Where(source => source.clip == clip).ToList()
						.ForEach(source =>
						{
							_sndObjectPool.Remove(source);
							Destroy(source.gameObject);
						});

					_externalAudioSources.Clear();

					_sounds.Where(pair => pair.Key.AudioSource.clip == clip).Select(pair => pair.Key)
						.ToList().ForEach(item =>
						{
							var coroutine = _sounds[item];
							if (coroutine != null)
							{
								StopCoroutine(coroutine);
							}

							_sounds.Remove(item);
							Destroy(item.AudioSource.gameObject);
						});
				}
			}
		}

		public bool PlayMusic(string id, float fadeDuration = 1, SystemLanguage language = SystemLanguage.Unknown,
			bool restart = true)
		{
			AudioClip clip = null;
			if (!string.IsNullOrEmpty(id))
			{
				if (!_registeredClips.TryGetValue(language, out var locale) || !locale.ContainsKey(id))
				{
					Debug.LogWarningFormat("There is no clip with id {0} for language {1}.",
						id, typeof(SystemLanguage).GetEnumName(language));

					if (_registeredClips.Any(p1 => p1.Value.TryGetValue(id, out clip)))
					{
						Debug.LogWarningFormat("Found clip with if {0} for other language.", id);
					}
				}
				else
				{
					clip = locale[id];
				}
			}

			if (_musicSource == null)
			{
				Assert.IsNull(_musicOldSource);
				if (clip != null)
				{
					_musicSource = CreateMusicAudioSource(clip, MusicVolume);
				}
			}
			else
			{
				if (!restart && _lastMusicId == id)
				{
					return true;
				}

				KillFadeCoroutine(_musicSource);
				if (_musicOldSource != null)
				{
					KillFadeCoroutine(_musicOldSource);
					Destroy(_musicOldSource.gameObject);
					_musicOldSource = null;
				}

				if (fadeDuration <= 0 || !MusicIsHeard)
				{
					_musicSource.Stop();
					if (clip != null)
					{
						_musicSource.clip = clip;
					}
					else
					{
						Destroy(_musicSource.gameObject);
						_musicSource = null;
						_lastMusicId = string.Empty;
					}
				}
				else
				{
					_musicOldSource = _musicSource;
					if (clip != null)
					{
						_musicSource = CreateMusicAudioSource(clip, 0);
					}
					else
					{
						_musicSource = null;
						_lastMusicId = string.Empty;
					}
				}
			}

			if (_musicOldSource != null)
			{
				if (_musicSource != null)
				{
					StartFadeCoroutine(_musicSource, MusicVolume, fadeDuration);
				}

				StartFadeCoroutine(_musicOldSource, 0, fadeDuration, () =>
				{
					Destroy(_musicOldSource.gameObject);
					_musicOldSource = null;
				});
			}

			if (_musicSource != null)
			{
				_musicSource.Play();
				_lastMusicId = id;
			}

			return true;
		}

		private IEnumerator FadeRoutine(AudioSource source, float destValue, float duration, Action callback)
		{
			var startVolume = source.volume;
			var delta = destValue - startVolume;
			var timesLeft = 0f;
			while (timesLeft < duration)
			{
				yield return null;
				timesLeft += Time.unscaledDeltaTime;
				var inc = Mathf.Clamp01(timesLeft / duration) * delta;
				source.volume = startVolume + inc;
			}

			source.volume = destValue;
			var r = _fadeRoutines.Remove(source);
			Assert.IsTrue(r, "AudioSource must be added to _fadeRoutines dictinary after the coroutine statrs.");
			callback?.Invoke();
		}

		private void KillFadeCoroutine(AudioSource source)
		{
			if (!_fadeRoutines.TryGetValue(source, out var coroutine)) return;
			StopCoroutine(coroutine);
			_fadeRoutines.Remove(source);
		}

		private void StartFadeCoroutine(AudioSource source, float destValue, float duration, Action callback = null)
		{
			if (_fadeRoutines.TryGetValue(source, out var coroutine))
			{
				// ReSharper disable once Unity.NoNullPropogation
				Debug.LogErrorFormat("Fade coroutine for AudioSource with clip {0} already started.",
					source.clip?.name ?? "???");
				StopCoroutine(coroutine);
			}

			_fadeRoutines[source] = StartCoroutine(FadeRoutine(source, destValue, duration, callback));
		}

		public event SoundStateChangedHandler PlaySoundEvent;

		public event SoundStateChangedHandler StopSoundEvent;

		public int PlaySound(string id, float muffleOthersPercent = 0, int priority = 0, int loopCount = 1,
			SystemLanguage language = SystemLanguage.Unknown, AudioSource audioSource = null)
		{
			AudioClip clip = null;
			if (!string.IsNullOrEmpty(id))
			{
				if (!_registeredClips.TryGetValue(language, out var locale) || !locale.ContainsKey(id))
				{
					Debug.LogWarningFormat("There is no clip with id {0} for language {1}.",
						id, typeof(SystemLanguage).GetEnumName(language));

					if (_registeredClips.Any(p1 => p1.Value.TryGetValue(id, out clip)))
					{
						Debug.LogWarningFormat("Found clip with if {0} for other language.", id);
					}
				}
				else
				{
					clip = locale[id];
				}
			}

			if (clip == null) return 0;

			var soundId = ++_currentId;

			var src = CreateSoundAudioSource(clip, loopCount, audioSource, soundId);
			var coroutine = loopCount > 0 ? StartCoroutine(ListenForEndOfClipRoutine(src, loopCount)) : null;

			muffleOthersPercent = Mathf.Clamp01(muffleOthersPercent);
			var soundItem = new SoundItem(src, soundId, priority, muffleOthersPercent >= MuffleMaxValue);
			_sounds.Add(soundItem, coroutine);

			while (_sounds.Count > SoundsLimit)
			{
				var item = _sounds.First();
				if (item.Value != null)
				{
					StopCoroutine(item.Value);
				}

				StopAndReturnToPool(item.Key.AudioSource, item.Key.SoundId);
				_sounds.Remove(item.Key);
			}

			src.Play();

			UpdateMuffle(soundId, muffleOthersPercent);
			UpdateMuting();

			PlaySoundEvent?.Invoke(soundId);
			return soundId;
		}

		public void StopSound(int soundId)
		{
			var item = _sounds.FirstOrDefault(pair => pair.Key.SoundId == soundId);
			if (item.Key == null) return;

			if (item.Value != null)
			{
				StopCoroutine(item.Value);
			}

			StopAndReturnToPool(item.Key.AudioSource, item.Key.SoundId);
			_sounds.Remove(item.Key);
			UpdateMuting();
		}

		public void SetMusicVolume(float value)
		{
			value = Mathf.Clamp01(value);
			var k = 1f - _mufflePercent;
			if (_musicSource != null)
			{
				_musicSource.volume = value * k;
			}

			if (Math.Abs(value - MusicVolume) >= 0.01f)
			{
				MusicVolume = value;
				PersistCurrentState();
			}
		}

		public void SetSoundVolume(float value)
		{
			value = Mathf.Clamp01(value);
			var k = 1f - _mufflePercent;
			foreach (var soundItem in _sounds.Keys)
			{
				soundItem.AudioSource.volume = soundItem.SoundId == _muffleSoundId ? value : value * k;
			}

			if (Math.Abs(value - SoundVolume) >= 0.01f)
			{
				SoundVolume = value;
				PersistCurrentState();
			}
		}

		public bool MuteMusic
		{
			get => _muteMusic;
			set
			{
				if (_muteMusic == value) return;
				_muteMusic = value;
				UpdateMuting();
				PersistCurrentState();
				MuteMusicChangedEvent?.Invoke(_muteMusic);
			}
		}

		public bool MuteSound
		{
			get => _muteSound;
			set
			{
				if (_muteSound == value) return;
				_muteSound = value;
				UpdateMuting();
				PersistCurrentState();
				MuteSoundChangedEvent?.Invoke(_muteSound);
			}
		}

		public event MuteChangedHandler MuteMusicChangedEvent;

		public event MuteChangedHandler MuteSoundChangedEvent;

		public bool HasClip(string id, SystemLanguage? language = null)
		{
			return language.HasValue
				? _registeredClips.TryGetValue(language.Value, out var locale) && locale.ContainsKey(id)
				: _registeredClips.Any(pair => pair.Value.ContainsKey(id));
		}

		// \IAudioManager

		private AudioSource CreateMusicAudioSource(AudioClip clip, float volume)
		{
			var src = new GameObject("MusicSource", typeof(SoundSourceController))
				.GetComponent<SoundSourceController>();
			DontDestroyOnLoad(src.gameObject);

			src.AudioSource.clip = clip;
			src.AudioSource.volume = volume;
			src.AudioSource.ignoreListenerVolume = true;
			src.AudioSource.loop = true;
			src.AudioSource.mute = !MusicIsHeard;

			return src.AudioSource;
		}

		private AudioSource CreateSoundAudioSource(AudioClip clip, int loopCount, AudioSource src, int id)
		{
			if (src != null || _sndObjectPool.Count > 0)
			{
				if (src == null)
				{
					src = _sndObjectPool[0];
					_sndObjectPool.RemoveAt(0);
					src.gameObject.SetActive(true);
				}
				else
				{
					if (_externalAudioSources.ContainsKey(src))
					{
						StopAndReturnToPool(src, _externalAudioSources[src]);
					}

					_externalAudioSources.Add(src, id);
				}

				src.clip = clip;
				src.volume = SoundVolume;
				src.mute = !SoundIsHeard;
				src.loop = loopCount > 1;
			}
			else
			{
				var srcCtrl = new GameObject("SoundSource", typeof(SoundSourceController))
					.GetComponent<SoundSourceController>();
				src = srcCtrl.AudioSource;

				src.clip = clip;
				src.volume = SoundVolume;
				src.ignoreListenerVolume = true;
				src.mute = !SoundIsHeard;
				src.loop = loopCount > 1;

				srcCtrl.DestroyEvent.AddListener(() =>
				{
					if (!_sndObjectPool.Remove(src))
					{
						var soundItem = _sounds.FirstOrDefault(pair => pair.Key.AudioSource == src).Key;
						if (soundItem != null)
						{
							var coroutine = _sounds[soundItem];
							if (coroutine != null)
							{
								StopCoroutine(coroutine);
							}

							UpdateMuffle(soundItem.SoundId, 0);
							_sounds.Remove(soundItem);
						}

						_fadeRoutines.Remove(src);
					}
				});
			}

			return src;
		}

		private IEnumerator ListenForEndOfClipRoutine(AudioSource src, int loopCount)
		{
			yield return new WaitForSeconds(src.clip.length * loopCount);

			var item = _sounds.First(pair => pair.Key.AudioSource == src);
			StopAndReturnToPool(item.Key.AudioSource, item.Key.SoundId);
			_sounds.Remove(item.Key);
			UpdateMuting();
		}

		private void StopAndReturnToPool(AudioSource src, int soundId)
		{
			src.Stop();
			if (_externalAudioSources.ContainsKey(src))
			{
				Assert.IsTrue(_externalAudioSources[src] == soundId);
				_externalAudioSources.Remove(src);
			}
			else
			{
				src.gameObject.SetActive(false);
				_sndObjectPool.Add(src);
			}

			UpdateMuffle(soundId, 0);
			StopSoundEvent?.Invoke(soundId);
		}

		private bool MusicIsHeard => !MuteMusic && _sounds.Count(pair => pair.Key.Exclusive) <= 0;

		private bool SoundIsHeard => !MuteSound && _sounds.Count(pair => pair.Key.Exclusive) <= 0;

		private void UpdateMuting()
		{
			if (_musicSource)
			{
				var musicIsMuting = !MusicIsHeard;
				_musicSource.mute = musicIsMuting;
				if (musicIsMuting && _musicOldSource)
				{
					KillFadeCoroutine(_musicSource);
					_musicSource.volume = MusicVolume;

					KillFadeCoroutine(_musicOldSource);
					Destroy(_musicOldSource.gameObject);
					_musicOldSource = null;
				}
			}

			var soundIsMuting = !SoundIsHeard;
			foreach (var pair in _sounds)
			{
				pair.Key.AudioSource.mute = soundIsMuting;
			}

			var item = _sounds.LastOrDefault(pair => pair.Key.Exclusive);
			if (!MuteSound && item.Key != null)
			{
				item.Key.AudioSource.mute = false;
			}
		}

		private void UpdateMuffle(int soundId, float mufflePercent)
		{
			if (mufflePercent < MuffleMinValue)
			{
				if (_muffleSoundId <= 0 || _muffleSoundId != soundId)
				{
					// Незначащее гашение, или значение не для гасящего звука.
					return;
				}

				_muffleSoundId = 0;
				_mufflePercent = 0;
			}
			else
			{
				if (_mufflePercent >= MuffleMaxValue)
				{
					// Гашение приравнивается к эксклюзивному воспроизведению звука.
					return;
				}

				_muffleSoundId = soundId;
				_mufflePercent = mufflePercent;
			}

			SetSoundVolume(SoundVolume);
			SetMusicVolume(MusicVolume);
		}

#if UNITY_EDITOR
		[MenuItem("Tools/Game Settings/Audio Manager")]
		private static void FindAndSelectWindowManager()
		{
			var instance = Resources.FindObjectsOfTypeAll<AudioManagerBase>().FirstOrDefault();
			if (!instance)
			{
				LoadAllPrefabs();
				instance = Resources.FindObjectsOfTypeAll<AudioManagerBase>().FirstOrDefault();
			}

			if (instance)
			{
				Selection.activeObject = instance;
				return;
			}

			Debug.LogError("Can't find prefab of AudioManager.");
		}

		private static void LoadAllPrefabs()
		{
			Directory.GetDirectories(Application.dataPath, @"Resources", SearchOption.AllDirectories)
				.Select(s => Directory.GetFiles(s, @"*.prefab", SearchOption.TopDirectoryOnly))
				.SelectMany(strings => strings.Select(Path.GetFileNameWithoutExtension))
				.Distinct().ToList().ForEach(s => Resources.LoadAll(s));
		}
#endif
	}
}