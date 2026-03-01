using HtmlAgilityPack;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlackoutBuster {
    internal static class Program {
        // Адреса сторінки з графіками Запоріжжяобленерго
        private static readonly string TargetUrl = "https://www.zoe.com.ua/outage/";
        // Ідентифікатор черги (групи) для пошуку
        private static readonly string GroupTag = "5.1";
        //ID каналу в Телеграме (публічний канал, тому вказую ID в коді)
        private static readonly string ChatId = "-1003611956747";
        // Файл для збереження стану між запусками
        private static readonly string StateFile = "state.json";

        // Налаштовуємо клієнт один раз при старті додатка
        private static readonly HttpClient HttpClient = new HttpClient {
            Timeout = TimeSpan.FromSeconds(120) // ScraperAPI потребує часу
        };

        // Налаштування сериалізації: гарний вигляд (Indented) та підтримка кирилиці (Unicode)
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
        };

        // Скомпільований регулярний вираз для очищення пробілів.
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        // Скомпільований регулярний вираз для пошуку тексту від групи 5.1 до наступної комбінації "цифра.цифра:" або кінця рядка
        private static readonly Regex GroupSearchRegex = new Regex($@"({GroupTag}:.*?(?=\d\.\d:|$))", RegexOptions.Compiled | RegexOptions.Singleline);

        // API-ключ із системних змінних середовища (Environment Variables)
        private static readonly string ScraperKey = Environment.GetEnvironmentVariable("SCRAPER_API_KEY") ?? "";
        // Токен для Телеграму із системних змінних середовища (Environment Variables)
        private static readonly string BotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";

        static Program() {
            // Встановлюємо стандартний User-Agent для ідентифікації клієнта
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        static async Task Main() {
            try {
                var currentState = await GetLatestGroupInfo();
                if (IsUpdateRequired(currentState)) {
                    await SendTelegramMessageAsync(currentState.ToString());
                    Console.WriteLine($"Notification sent.");
                } else {
                    Console.WriteLine($"No new updates for group.");
                }
                SaveState(currentState);
            }
            catch (Exception ex) {
                Console.WriteLine($"::error::[Error]: {ex.Message}");
                // Гарантуємо, що GitHub Actions побачить статус "Failure"
                Environment.Exit(1);
            }
        }

        private static async Task SendTelegramMessageAsync(string message) {
            if (string.IsNullOrEmpty(BotToken)) {
                throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is not set. The application cannot send notifications.");
            }

            // URL-адреса API Telegram для надсилання повідомлень
            string url = $"https://api.telegram.org/bot{BotToken}/sendMessage";

            var payload = new {
                chat_id = ChatId,
                text = message,
            };

            // Конвертація у JSON та відправка
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"Telegram API Error: {response.StatusCode}");
            }
        }

        private static async Task<ScheduleState> GetLatestGroupInfo() {
            // Завантаження HTML-документа
            var doc = await DownloadHtmlAsync();

            // Перебір усіх вузлів (тегів) документа
            var targetHeaderNode = doc.DocumentNode.Descendants()
                .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element // Шукаємо тільки елементи (теги)
                    && n.InnerText.Contains("ГПВ") // Які містять абревіатуру ГПВ (Графіки погодинних відключень)
                    && (n.InnerText.Contains("ОНОВЛЕНО") || n.InnerText.Contains("ДІЯТИМУТЬ")) // Та ключові слова
                    && n.InnerText.Length < 200); // Обмежуємо довжину тексту, щоб не захопити величезні блоки

            if (targetHeaderNode == null) {
                throw new InvalidOperationException("Could not find a valid schedule header on the page.");
            }

            // Видалення зайвих пробілів та переносів із заголовка
            string headerText = WhitespaceRegex.Replace(targetHeaderNode.InnerText.Trim(), " ");

            // Отримання контейнера предка, у якому лежить текст із чергами
            var parentContainer = targetHeaderNode.ParentNode;
            // Цикл пошуку вгору по дереву до потрібної групи
            while (parentContainer != null &&
                 parentContainer.Name != "div" &&
                 parentContainer.Name != "article") {
                parentContainer = parentContainer.ParentNode;
            }

            // Якщо контейнер не знайдено — це структурна помилка (сайт змінився)
            if (parentContainer == null) {
                throw new InvalidOperationException("Could not find a valid container for the schedule.");
            }

            // Витягнення всього тексту зі знайденого контейнера
            string containerText = parentContainer!.InnerText;
            // Пошук тексту для групи 5.1
            var match = GroupSearchRegex.Match(containerText);

            // Якщо регулярний вираз знайшов збіг
            string scheduleInfo = match.Success
                ? WhitespaceRegex.Replace(match.Groups[1].Value.Trim(), " ").TrimEnd(',', ' ')
                : $"{GroupTag}: Відключень наразі не заплановано.";

            // Формування підсумкового повідомлення (заголовок + графік)
            var state = new ScheduleState {
                Date = ExtractDateKey(headerText),
                HeaderText = headerText,
                ScheduleInfo = scheduleInfo
            };
            return state;
        }

        private static bool IsUpdateRequired(ScheduleState currentState) {
            ScheduleState? loadedData = LoadState();

            // Якщо loadedData == null, треба відправити повідомлення
            if (loadedData is not ScheduleState loadedState) {
                return true;
            }

            // Якщо новий день, треба відправити повідомлення
            if (loadedState.Date != currentState.Date)
                return true;

            // Якщо графік змінився, треба відправити повідомлення
            if (loadedState.ScheduleInfo != currentState.ScheduleInfo)
                return true;

            return false;
        }

        private static ScheduleState? LoadState() {
            if (!File.Exists(StateFile))
                return null;
            try {
                string json = File.ReadAllText(StateFile);
                return JsonSerializer.Deserialize<ScheduleState>(json, JsonOptions);
            }
            catch (Exception ex) {
                throw new InvalidOperationException($"Error reading file {StateFile}. {ex.Message}");
            }
        }

        private static void SaveState(ScheduleState state) {
            try {
                string json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(StateFile, json);
            }
            catch (Exception ex) {
                throw new InvalidOperationException($"Save Error: Could not write to {StateFile}. {ex.Message}");
            }
        }

        private static int ExtractDateKey(string header) {
            if (string.IsNullOrWhiteSpace(header)) {
                throw new InvalidOperationException("Header is empty.");
            }

            // Пробуємо взяти перші два символи(на випадок рядка виду "6 СІЧНЯ ...")
            string rawDayValue = new string(header.TrimStart().Take(2).ToArray());
            if (int.TryParse(rawDayValue, out int dateValue)) {
                return dateValue;
            }

            // Шукаємо ключове слово " НА " (на випадок рядка виду "ОНОВЛЕНО ГПВ НА 6 СІЧНЯ ...")
            int index = header.IndexOf(" НА ", StringComparison.OrdinalIgnoreCase);
            if (index != -1) {
                // Позиція одразу після " НА " (4 символи: пробіл + Н + А + пробіл)
                int startPos = index + 4;

                // Перевіряємо, чи є в рядку символи після знайденого індексу
                if (header.Length >= startPos) {
                    // Витягуємо цифри після " НА ", ігноруючи можливі початкові пробіли
                    string rawDate = new string(header.Substring(startPos).TrimStart().Take(2).ToArray());
                    if (int.TryParse(rawDate, out dateValue)) {
                        return dateValue;
                    }
                }
            }

            // Якщо жоден спосіб не спрацював — це критична зміна формату сайту
            throw new InvalidOperationException($"Could not extract date from header: {header}");
        }

        private static async Task<HtmlDocument> DownloadHtmlAsync() {
            if (string.IsNullOrEmpty(ScraperKey)) {
                throw new InvalidOperationException("SCRAPER_API_KEY environment variable is not set.");
            }

            // Формування проксі-запиту з використанням Escaped URL для коректної передачі параметрів
            // Параметри: premium=true (використання резидентських IP), country_code=ua (геоприв'язка)
            string proxyUrl = $"http://api.scraperapi.com?api_key={ScraperKey}" +
                              $"&url={Uri.EscapeDataString(TargetUrl)}" +
                              $"&premium=true" +
                              $"&country_code=ua";

            try {
                // Виконання асинхронного GET-запиту через проксі-шлюз
                string html = await HttpClient.GetStringAsync(proxyUrl);

                if (string.IsNullOrEmpty(html)) {
                    throw new InvalidOperationException("Received empty response from ScraperAPI.");
                }

                HtmlDocument doc = new HtmlDocument();
                // Завантаження HTML та перетворення спецсимволів (наприклад, &nbsp; у пробіли)
                doc.LoadHtml(HtmlEntity.DeEntitize(html));
                return doc;
            }
            catch (HttpRequestException ex) {
                throw new InvalidOperationException($"[Network Error]: {ex.Message}");
            }
        }

        private readonly record struct ScheduleState {
            public int Date { get; init; }
            public string HeaderText { get; init; }
            public string ScheduleInfo { get; init; }
            public override string ToString() => $"{HeaderText}\n\n{ScheduleInfo}";
        }
    }
}