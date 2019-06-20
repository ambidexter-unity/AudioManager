#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UniRx;
using UnityEngine;
using Zenject;
using Extensions;
using UniRx.Triggers;
using UnityEngine.Assertions;

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

	public class AudioManager : ScriptableObjectInstaller<AudioManager>, IAudioManager
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


		private static int _currentId;

		// ReSharper disable once InconsistentNaming
		private const int SoundsLimit = 8;

		private const float MuffleMinValue = 0.1f;
		private const float MuffleMaxValue = 0.99f;

		private readonly FloatReactiveProperty _musicVolume = new FloatReactiveProperty(1f);
		private readonly FloatReactiveProperty _soundVolume = new FloatReactiveProperty(1f);

		private const string AudioKey = "tamafish_audio";

		private AudioSource _musicSource;
		private AudioSource _musicOldSource;

		private bool _muteMusic;
		private bool _muteSound;

		private readonly List<AudioSource> _sndObjectPool = new List<AudioSource>(SoundsLimit);

		private readonly Dictionary<SystemLanguage, Dictionary<string, AudioClip>> _registeredClips =
			new Dictionary<SystemLanguage, Dictionary<string, AudioClip>>();

		private readonly SortedDictionary<SoundItem, IDisposable> _sounds =
			new SortedDictionary<SoundItem, IDisposable>();

		private int _muffleSoundId;
		private float _mufflePercent;

#pragma warning disable 649
		[Header("Global clips"), SerializeField]
		private AudioLocal[] _locales = new AudioLocal[0];
#pragma warning restore 649

		public override void InstallBindings()
		{
			Container.Bind<IAudioManager>().FromInstance(this).AsSingle();

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

		public IReadOnlyReactiveProperty<float> MusicVolume => _musicVolume;
		public IReadOnlyReactiveProperty<float> SoundVolume => _soundVolume;

		private void PersistCurrentState()
		{
			var data = JsonUtility.ToJson(new PersistentData
			{
				_muteMusic = MuteMusic,
				_muteSound = MuteSound,
				_musicVolume = MusicVolume.Value,
				_soundVolume = SoundVolume.Value
			});
			PlayerPrefs.SetString(AudioKey, data);
			PlayerPrefs.Save();
		}

		private void RestorePersistingState()
		{
			if (!PlayerPrefs.HasKey(AudioKey)) return;
			var data = JsonUtility.FromJson<PersistentData>(PlayerPrefs.GetString(AudioKey));
			_musicVolume.SetValueAndForceNotify(data._musicVolume);
			_soundVolume.SetValueAndForceNotify(data._soundVolume);
			_muteMusic = data._muteMusic;
			_muteSound = data._muteSound;
		}

#if UNITY_EDITOR
		private const string ManagerPath = "Assets/Scripts/Common/Manager";

		[MenuItem("Tools/Game Settings/Audio Manager Settings")]
		private static void GetAndSelectSettingsInstance()
		{
			EditorUtility.FocusProjectWindow();
			Selection.activeObject = InspectorExtensions.FindOrCreateNewScriptableObject<AudioManager>(ManagerPath);
		}
#endif

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
						DOTween.Kill(_musicSource);
						Destroy(_musicSource.gameObject);
						_musicSource = null;
						continue;
					}

					if (_musicOldSource != null && _musicOldSource.clip == clip)
					{
						DOTween.Kill(_musicOldSource);
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

					_sounds.Where(pair => pair.Key.AudioSource.clip == clip).Select(pair => pair.Key)
						.ToList().ForEach(item =>
						{
							_sounds[item].Dispose();
							_sounds.Remove(item);
							Destroy(item.AudioSource.gameObject);
						});
				}
			}
		}

		public bool PlayMusic(string id, float fadeDuration = 1, SystemLanguage language = SystemLanguage.Unknown)
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
					_musicSource = CreateMusicAudioSource(clip, MusicVolume.Value);
				}
			}
			else
			{
				DOTween.Kill(_musicSource);
				if (_musicOldSource != null)
				{
					DOTween.Kill(_musicOldSource);
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
					}
				}
			}

			if (_musicOldSource != null)
			{
				if (_musicSource != null)
				{
					_musicSource.DOFade(_musicVolume.Value, fadeDuration).SetEase(Ease.Linear);
				}

				_musicOldSource.DOFade(0, fadeDuration).SetEase(Ease.Linear).OnComplete(() =>
				{
					Destroy(_musicOldSource.gameObject);
					_musicOldSource = null;
				});
			}

			if (_musicSource != null)
			{
				_musicSource.Play();
			}

			return true;
		}

		public event Action<int> OnPlaySound;

		public event Action<int> OnStopSound;

		public int PlaySound(string id, float muffleOthersPercent = 0, int priority = 0, int loopCount = 1,
			SystemLanguage language = SystemLanguage.Unknown)
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

			var src = CreateSoundAudioSource(clip, loopCount);
			var handler = loopCount > 0 ? ListenForEndOfClip(src, loopCount) : null;

			muffleOthersPercent = Mathf.Clamp01(muffleOthersPercent);
			var soundItem = new SoundItem(src, soundId, priority, muffleOthersPercent >= MuffleMaxValue);
			_sounds.Add(soundItem, handler);

			while (_sounds.Count > SoundsLimit)
			{
				var item = _sounds.First();
				item.Value?.Dispose();
				StopAndReturnToPool(item.Key.AudioSource, item.Key.SoundId);
				_sounds.Remove(item.Key);
			}

			src.Play();

			UpdateMuffle(soundId, muffleOthersPercent);
			UpdateMuting();

			OnPlaySound?.Invoke(soundId);
			return soundId;
		}

		public void StopSound(int soundId)
		{
			var item = _sounds.FirstOrDefault(pair => pair.Key.SoundId == soundId);
			if (item.Key == null) return;

			item.Value?.Dispose();
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

			if (Math.Abs(value - MusicVolume.Value) >= 0.01f)
			{
				_musicVolume.SetValueAndForceNotify(value);
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

			if (Math.Abs(value - SoundVolume.Value) >= 0.01f)
			{
				_soundVolume.SetValueAndForceNotify(value);
				PersistCurrentState();
			}
		}

		public bool MuteMusic
		{
			set
			{
				if (_muteMusic == value) return;
				_muteMusic = value;
				UpdateMuting();
				PersistCurrentState();
			}
			get => _muteMusic;
		}

		public bool MuteSound
		{
			set
			{
				if (_muteSound == value) return;
				_muteSound = value;
				UpdateMuting();
				PersistCurrentState();
			}
			get => _muteSound;
		}

		public bool HasClip(string id, SystemLanguage? language = null)
		{
			return language.HasValue
				? _registeredClips.TryGetValue(language.Value, out var locale) && locale.ContainsKey(id)
				: _registeredClips.Any(pair => pair.Value.ContainsKey(id));
		}

		// \IAudioManager

		private AudioSource CreateMusicAudioSource(AudioClip clip, float volume)
		{
			var src = Container.InstantiateComponentOnNewGameObject<SoundSourceController>("MusicSource").AudioSource;

			src.clip = clip;
			src.volume = volume;
			src.ignoreListenerVolume = true;
			src.loop = true;
			src.mute = !MusicIsHeard;

			return src;
		}

		private AudioSource CreateSoundAudioSource(AudioClip clip, int loopCount)
		{
			AudioSource src;
			if (_sndObjectPool.Count > 0)
			{
				src = _sndObjectPool[0];
				_sndObjectPool.RemoveAt(0);

				src.clip = clip;
				src.volume = SoundVolume.Value;
				src.gameObject.SetActive(true);
				src.mute = !SoundIsHeard;
				src.loop = loopCount > 1;
			}
			else
			{
				var container = Container.AncestorContainers.FirstOrDefault() ?? Container;
				src = container.InstantiateComponentOnNewGameObject<SoundSourceController>("SoundSource").AudioSource;

				src.clip = clip;
				src.volume = SoundVolume.Value;
				src.ignoreListenerVolume = true;
				src.mute = !SoundIsHeard;
				src.loop = loopCount > 1;

				IDisposable d = null;
				d = src.OnDestroyAsObservable().Subscribe(unit =>
				{
					// ReSharper disable once AccessToModifiedClosure
					d?.Dispose();
					if (!_sndObjectPool.Remove(src))
					{
						var soundItem = _sounds.FirstOrDefault(pair => pair.Key.AudioSource == src).Key;
						if (soundItem != null)
						{
							_sounds[soundItem].Dispose();
							_sounds.Remove(soundItem);
						}
					}
				});
			}

			return src;
		}

		private IDisposable ListenForEndOfClip(AudioSource src, int loopCount)
		{
			IDisposable d = null;
			d = Observable.Timer(TimeSpan.FromSeconds(src.clip.length * loopCount), Scheduler.MainThreadIgnoreTimeScale)
				.Subscribe(l =>
				{
					// ReSharper disable once AccessToModifiedClosure
					d?.Dispose();
					var item = _sounds.First(pair => pair.Key.AudioSource == src);
					StopAndReturnToPool(item.Key.AudioSource, item.Key.SoundId);
					_sounds.Remove(item.Key);
					UpdateMuting();
				});

			return d;
		}

		private void StopAndReturnToPool(AudioSource src, int soundId)
		{
			src.Stop();
			src.gameObject.SetActive(false);
			_sndObjectPool.Add(src);
			UpdateMuffle(soundId, 0);
			OnStopSound?.Invoke(soundId);
		}

		private bool MusicIsHeard => !_muteMusic && _sounds.Count(pair => pair.Key.Exclusive) <= 0;

		private bool SoundIsHeard => !_muteSound && _sounds.Count(pair => pair.Key.Exclusive) <= 0;

		private void UpdateMuting()
		{
			if (_musicSource != null)
			{
				var musicIsMuting = !MusicIsHeard;
				_musicSource.mute = musicIsMuting;
				if (musicIsMuting && _musicOldSource != null)
				{
					DOTween.Kill(_musicSource);
					_musicSource.volume = MusicVolume.Value;

					DOTween.Kill(_musicOldSource);
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
			if (!_muteSound && item.Key != null)
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

			SetSoundVolume(SoundVolume.Value);
			SetMusicVolume(MusicVolume.Value);
		}
	}
}