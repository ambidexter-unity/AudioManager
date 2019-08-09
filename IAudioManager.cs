using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;

namespace Common.Audio
{
    public interface IAudioManager
    {
        /// <summary>
        /// Зарегистрировать аудиоклипы.
        /// </summary>
        /// <param name="clips">Список клипов в формате [идентификатор]:[клип].</param>
        /// <param name="language">Язык, которому соответствуют регистрируемые клипы.</param>
        void RegisterClips(Dictionary<string, AudioClip> clips, SystemLanguage language = SystemLanguage.Unknown);

        /// <summary>
        /// Удалить регистрацию для клипов.
        /// </summary>
        /// <param name="clipIds">Список идентификаторов удаляемых клипов.</param>
        void UnregisterClips(IEnumerable<string> clipIds);

        /// <summary>
        /// Играть фоновую музыку.
        /// </summary>
        /// <param name="id">Идентификатор клипа в AudioSettings.</param>
        /// <param name="fadeDuration">Время фейда с предыдущим треком.</param>
        /// <param name="language">Язык, для которого воспроизводится музыка.</param>
        /// <returns>Возвращает <code>true</code>, если музыка успешно воспроизведена.</returns>
        bool PlayMusic(string id, float fadeDuration = 1f, SystemLanguage language = SystemLanguage.Unknown);

        /// <summary>
        /// Играть звук.
        /// </summary>
        /// <param name="id">Идентификатор клипа в AudioSettings, или null, если музыку следует выключить.</param>
        /// <param name="muffleOthersPercent">Процент гашения (0...1) других звуков на время проигрывания звука.</param>
        /// <param name="priority">Приоритет при проигрывании (звуки с высоким приоритетом в последнюю очередь
        /// удаляются при превышении лимита воспроизводимых звуков).</param>
        /// <param name="loopCount">Количество воспроизведений, бесконечно, если 0.</param>
        /// <param name="language">Язык, для которого воспроизводится звук.</param>
        /// <param name="audioSource">Источник звука, <code>null</code>, если источник не известен.</param>
        /// <returns>Возвращает Уникальный идентификатор воспроизводимого звука,
        /// или 0, если звук не воспроизведен.</returns>
        int PlaySound(string id, float muffleOthersPercent = 0, int priority = 0,
            int loopCount = 1, SystemLanguage language = SystemLanguage.Unknown, AudioSource audioSource = null);

        /// <summary>
        /// Проверить наличие клипа для указанной локализации.
        /// </summary>
        /// <param name="id">Идентификатор клипа.</param>
        /// <param name="language">Язык, для которого проверяется наличие клипа, если <code>null</code>,
        /// то возвращается <code>true</code> при наличии клипа для любого языка.</param>
        /// <returns>Возвращает <code>true</code>, если клип с указанным идентификатором найден.</returns>
        bool HasClip(string id, SystemLanguage? language = null);

        /// <summary>
        /// Остановить воспроизведение звука.
        /// </summary>
        /// <param name="soundId">Идентификатор звука, полученный из PlaySound().</param>
        void StopSound(int soundId);

        /// <summary>
        /// Задать новое значение уровня громкости для музыки.
        /// </summary>
        /// <param name="value">Новое значение уровня громкости (0...1).</param>
        void SetMusicVolume(float value);

        /// <summary>
        /// Задать новое значение уровня громкости для звуков.
        /// </summary>
        /// <param name="value">Новое значение уровня громкости (0...1).</param>
        void SetSoundVolume(float value);

        /// <summary>
        /// Заглушить музыку.
        /// </summary>
        bool MuteMusic { set; get; }

        /// <summary>
        /// Заглушить звуки.
        /// </summary>
        bool MuteSound { set; get; }

        /// <summary>
        /// Реактивное свойство, отображающее текущий уровень громкости для музыки.
        /// </summary>
        IReadOnlyReactiveProperty<float> MusicVolume { get; }

        /// <summary>
        /// Реактивное свойство, отображающее текущий уровень громкости для звуков.
        /// </summary>
        IReadOnlyReactiveProperty<float> SoundVolume { get; }

        /// <summary>
        /// Событие начала воспроизведения звука, принимает в качестве аргумента идентификатор звука.
        /// </summary>
        event Action<int> OnPlaySound;

        /// <summary>
        /// Событие окончания воспроизведения звука, принимает в качестве аргумента идентификатор звука.
        /// </summary>
        event Action<int> OnStopSound;
    }
}