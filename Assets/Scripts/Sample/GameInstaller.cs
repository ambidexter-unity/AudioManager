using Common.Audio;
using Zenject;

namespace Sample
{
	public class GameInstaller : MonoInstaller<GameInstaller>
	{
		public override void InstallBindings()
		{
			Container.Bind<IAudioManager>().FromComponentInNewPrefabResource(@"AudioManager").AsSingle();
		}
	}
}