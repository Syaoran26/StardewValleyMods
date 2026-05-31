using Microsoft.Xna.Framework.Audio;
using StardewModdingAPI;
using StardewValley;
using System.Text;
using System.Text.RegularExpressions;

namespace VietnameseCharacterTTS.Framework
{
    internal class TextToSpeech
    {
        private string _cacheDirectory;
        private string _apiKey;
        private double _speed;
        private IMonitor? _monitor;

        private readonly HttpClient _httpClient = new HttpClient();
        private SoundEffectInstance? _currentSoundInstance;

        public TextToSpeech(string directory, string apiKey, double speed = 1.15, IMonitor? monitor = null)
        {
            _cacheDirectory = directory;
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            };

            _apiKey = apiKey;
            _speed = speed;
            _monitor = monitor;
        }

        public async Task ProcessTTS(string text, NPC npc)
        {
            if (string.IsNullOrEmpty(_apiKey)) return;

            try
            {
                // 1. Làm sạch các thẻ điều hướng hội thoại của game ($h, $c, $b, ...)
                text = Regex.Replace(text, @"\$[a-zA-Z]", "").Trim();

                // 2. TỐI ƯU CHIẾN LƯỢC: Escape các ký tự đặc biệt để chuỗi JSON gửi đi không bị gãy cấu trúc
                // Hội thoại trong game rất hay có dấu xuống dòng (\n), dấu tab (\t) hoặc dấu ngoặc kép (")
                string safeText = text
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", " ")
                    .Replace("\r", " ")
                    .Replace("\t", " ");
                string npcName = npc.Name;

                string fileName = GetSafeCustomHash(npcName + "_" + safeText) + ".wav";
                string filePath = Path.Combine(_cacheDirectory, fileName);

                if (File.Exists(filePath))
                {
                    PlayAudioFile(filePath);
                    return;
                }

                var gender = npc.Gender;

                // 3. Chọn cấu hình giọng đọc dựa trên Gender của NPC
                string voiceName = (gender == Gender.Female) ? "vi-VN-Wavenet-A" : "vi-VN-Wavenet-B";
                double speed = _speed;

                try
                {
                    // 2. Kiểm tra độ tuổi (Age) để ép giọng đặc thù
                    if (npc.Age == 2) // Nếu là TRẺ EM (Jas, Vincent,...)
                    {
                        // Trẻ em dùng giọng Standard để có cảm giác thanh, cao và hơi ngây ngô
                        voiceName = (gender == Gender.Female) ? "vi-VN-Standard-A" : "vi-VN-Standard-C";
                        speed = _speed + 0.05;
                    }
                    else if (npc.Age == 1) // Nếu là THANH THIẾU NIÊN (Haley, Abigail, Sam...)
                    {
                        // Thanh niên ưu tiên giọng Neural2 thế hệ mới cực kỳ trẻ trung và bắt trend
                        voiceName = (gender == Gender.Female) ? "vi-VN-Neural2-A" : "vi-VN-Neural2-B";
                        speed = _speed + 0.1;
                    }
                    else // NGƯỜI LỚN HOẶC NGƯỜI GIÀ (Age == 0)
                    {
                        // Nếu là người hướng nội hoặc nhút nhát (Shy), chọn giọng WaveNet trầm ấm, đĩnh đạc
                        if (npc.SocialAnxiety == 1)
                        {
                            voiceName = (gender == Gender.Female) ? "vi-VN-Wavenet-C" : "vi-VN-Neural2-D";
                            speed = _speed - 0.05;
                        }
                        else // Người lớn hướng ngoại hoặc bình thường
                        {
                            voiceName = (gender == Gender.Female) ? "vi-VN-Wavenet-A" : "vi-VN-Wavenet-B";
                        }
                    }

                    // 3. ĐẶC CÁCH CHIẾN LƯỢC: Nếu là các nhân vật siêu đặc biệt (Quái vật, Pháp sư, thực thể không rõ giới tính)
                    if (npcName == "Wizard" || npcName == "Krobus" || gender == Gender.Undefined)
                    {
                        voiceName = "vi-VN-Wavenet-D"; // Ép về giọng Nam trầm dày nhất của Google để tạo độ huyền bí
                        speed = _speed;
                    }
                }
                catch (Exception)
                {
                    // Phòng hờ nếu mod nào đó cấu hình lỗi thuộc tính, tự động về giọng mặc định an toàn
                    voiceName = (gender == Gender.Female) ? "vi-VN-Standard-A" : "vi-VN-Standard-B";
                }

                // Tự dựng cấu trúc chuỗi JSON (Bổ sung thêm speakingRate vào audioConfig)
                string jsonPayload = "{" +
                    "\"input\":{\"text\":\"" + safeText + "\"}," +
                    "\"voice\":{\"languageCode\":\"vi-VN\",\"name\":\"" + voiceName + "\"}," +
                    "\"audioConfig\":{" +
                        "\"audioEncoding\":\"LINEAR16\"," +
                        "\"speakingRate\":" + speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    "}" +
                "}";

                string url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={_apiKey}";
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync();
                    _monitor?.Log($"Google API trả về lỗi: {response.StatusCode} - Chi tiết: {errContent}", LogLevel.Error);
                    return;
                }

                string responseString = await response.Content.ReadAsStringAsync();

                //Phân tích cú pháp chuỗi JSON thô để lấy đoạn mã hóa Base64 của file âm thanh
                string base64Audio = responseString.Split("\"audioContent\": \"")[1].Split("\"")[0];
                byte[] audioBytes = Convert.FromBase64String(base64Audio);

                await File.WriteAllBytesAsync(filePath, audioBytes);
                PlayAudioFile(filePath);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Lỗi kết nối HTTP hoặc xử lý dữ liệu: {ex.Message}", LogLevel.Error);
            }
        }

        private void PlayAudioFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                // TỐI ƯU NGẮT ÂM THANH: Sử dụng SoundEffectInstance của MonoGame để kiểm soát lệnh Dừng
                if (_currentSoundInstance != null)
                {
                    _currentSoundInstance.Stop();
                    _currentSoundInstance.Dispose();
                    _currentSoundInstance = null;
                }

                byte[] audioBytes = File.ReadAllBytes(filePath);
                using (MemoryStream ms = new MemoryStream(audioBytes))
                {
                    var soundEffect = SoundEffect.FromStream(ms);
                    _currentSoundInstance = soundEffect.CreateInstance();
                    _currentSoundInstance.Volume = 1.0f;
                    _currentSoundInstance.Play(); // Phát âm thanh câu mới
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Loi Audio: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Thuật toán băm chuỗi DJB2 (Tự chế bằng toán học thuần túy)
        /// Không gọi tới System.Security.Cryptography nên an toàn 100% với SMAPI
        /// </summary>
        private string GetSafeCustomHash(string input)
        {
            uint hash = 5381;
            foreach (char c in input)
            {
                hash = ((hash << 5) + hash) + c; /* hash * 33 + c */
            }
            return hash.ToString();
        }
    }
}
