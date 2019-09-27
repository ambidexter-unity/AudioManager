using Common.Audio;
using Zenject;

namespace Sample
{
	public class OtherSceneBehaviour : MonoInstaller<OtherSceneBehaviour>
	{
#pragma warning disable 649
		[Inject] private readonly IAudioManager _audioManager;
#pragma warning restore 649

		public override void InstallBindings()
		{
		}

		public void PlayPhrase1()
		{
			_audioManager.PlaySound("phrase_1", 0.9f);
		}

		public void PlayPhrase2()
		{
			_audioManager.PlaySound("phrase_2", 0.9f);
		}

		public void PlayPhrase3()
		{
			_audioManager.PlaySound("phrase_3", 0.9f);
		}
	}
}