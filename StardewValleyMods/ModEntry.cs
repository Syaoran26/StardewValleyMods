using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Integrations.GenericModConfigMenu;
using StardewValley;
using StardewValley.Menus;
using VietnameseCharacterTTS.Framework;

namespace VietnameseCharacterTTS
{
    public class ModEntry : Mod
    {
        private TextToSpeech _tts;
        private ModConfig _config = null;
        private string _lastSpokenText;

        public override void Entry(IModHelper helper)
        {
            try
            {
                _config = helper.ReadConfig<ModConfig>();

                string cacheDirectory = Path.Combine(helper.DirectoryPath, "AudioCache");
                _tts = new TextToSpeech(
                    cacheDirectory,
                    _config.ApiKey,
                    _config.Speed
                );

                helper.Events.GameLoop.GameLaunched += OnGameLaunched;
                helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

                Monitor.Log($"Mod is ready!", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error to run mod: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => _config = new ModConfig(),
                save: () => Helper.WriteConfig(_config)
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Example string",
                getValue: () => _config.ApiKey,
                setValue: value => _config.ApiKey = value
            );
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            if (_tts == null) return;

            if (Game1.activeClickableMenu is DialogueBox dialogueBox)
            {
                var characterDialogue = dialogueBox.characterDialogue;

                // Chỉ xử lý hội thoại có NPC thực sự, bỏ qua hộp thoại hệ thống
                if (characterDialogue == null || characterDialogue.speaker == null)
                {
                    return;
                }

                NPC speakerNPC = characterDialogue.speaker;

                // Sửa lỗi chính tả: Trong Stardew Valley API, tên hàm viết hoa chữ cái đầu: GetCurrentDialogue()
                string textToSpeak = characterDialogue.getCurrentDialogue();

                if (string.IsNullOrWhiteSpace(textToSpeak) || textToSpeak == _lastSpokenText) return;
                _lastSpokenText = textToSpeak;

                // Chạy ngầm xử lý gửi sang Google TTS để tránh nghẽn khung hình game
                Task.Run(async () => await _tts.ProcessTTS(textToSpeak, speakerNPC));
            }
            else
            {
                if (!string.IsNullOrEmpty(_lastSpokenText))
                {
                    _lastSpokenText = "";
                }
            }
        }
    }
}
