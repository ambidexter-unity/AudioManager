using Common.Audio;

namespace Sample
{
	public class AudioManager : AudioManagerBase
	{
		protected override string AudioPersistKey => @"test_audio_key";
		protected override int SoundsLimit => 8;
	}
}