using Common.Audio;
using Zenject;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sample
{
	public class SampleSceneBehaviour : MonoInstaller<SampleSceneBehaviour>
	{
#pragma warning disable 649
		[SerializeField] private Toggle _ruToggle;
		[SerializeField] private Toggle _enToggle;

		[Inject] private readonly IAudioManager _audioManager;
#pragma warning restore 649

		public override void InstallBindings()
		{
		}

		public void PlayMusic1()
		{
			_audioManager.PlayMusic("music_1");
		}

		public void PlayMusic2()
		{
			_audioManager.PlayMusic("music_2");
		}

		public void PlayPhrase1()
		{
			_audioManager.PlaySound("phrase_1", 0.9f,
				language: _ruToggle.isOn ? SystemLanguage.Russian : SystemLanguage.English);
		}

		public void PlayPhrase2()
		{
			_audioManager.PlaySound("phrase_2", 0.9f,
				language: _ruToggle.isOn ? SystemLanguage.Russian : SystemLanguage.English);
		}

		public void PlayPhrase3()
		{
			_audioManager.PlaySound("phrase_3", 0.9f,
				language: _ruToggle.isOn ? SystemLanguage.Russian : SystemLanguage.English);
		}

		public void PlaySound()
		{
			_audioManager.PlaySound("sound", 0.9f);
		}

		public void LoadOtherScene()
		{
			SceneManager.LoadSceneAsync("OtherScene");
		}
	}
}