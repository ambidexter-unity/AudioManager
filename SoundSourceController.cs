using System;
using UniRx;
using UniRx.Triggers;
using UnityEngine;

namespace Common.Audio
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(AudioSource))]
	public class SoundSourceController : MonoBehaviour
	{
		private IDisposable _onDestroyHandler;
		private Transform _listener;

		public AudioSource AudioSource => GetComponent<AudioSource>();

		private void Start()
		{
			_listener = FindObjectOfType<AudioListener>()?.transform;
			if (_listener != null)
			{
				_onDestroyHandler = _listener.gameObject.OnDestroyAsObservable()
					.Subscribe(unit =>
					{
						_listener = null;
						_onDestroyHandler?.Dispose();
						_onDestroyHandler = null;
					});
			}
			else
			{
				Debug.LogWarning("Can't find AudioListener.");
			}
		}

		private void OnDestroy()
		{
			_onDestroyHandler?.Dispose();
			_onDestroyHandler = null;
		}

		private void Update()
		{
			if (_listener == null)
			{
				_listener = FindObjectOfType<AudioListener>()?.transform;
				if (_listener == null) return;
			}

			transform.position = _listener.position;
		}
	}
}