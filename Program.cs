using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace BlackoutBuster {
    internal static class Program {
        // Адреса сторінки з графіками Запоріжжяобленерго
        private static readonly string TargetUrl = "https://www.zoe.com.ua/outage/";
        // Ідентифікатор черги (групи) для пошуку
        private static readonly string GroupTag = "5.1";

        // Отримання API-ключа із системних змінних середовища (Environment Variables)
        private static readonly string ScraperKey = Environment.GetEnvironmentVariable("SCRAPER_API_KEY");

        // Скомпільований регулярний вираз для очищення пробілів.
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        // Скомпільований регулярний вираз для пошуку тексту від групи 5.1 до початку наступної (5.2)
        private static readonly Regex GroupSearchRegex = new Regex($@"({GroupTag}:.*?(?=5.2:|$))", RegexOptions.Compiled | RegexOptions.Singleline);

        static async Task Main() {
            try {
                await GetLatestGroupInfo();
            }
            catch (Exception ex) {
                Console.WriteLine($"[Error]: {ex.Message}");
            }
        }

        private static async Task GetLatestGroupInfo() {
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

            // Отримання батьківського контейнера, в якому лежить текст із чергами
            var parentContainer = targetHeaderNode.ParentNode;
            // Цикл пошуку вгору по дереву до потрібної групи
            while (parentContainer != null && !parentContainer.InnerText.Contains(GroupTag)) {
                // Перехід до наступного батька вище за ієрархією
                parentContainer = parentContainer.ParentNode;

                // Перевірка: чи не вилетіли ми за межі контенту (дійшли до "самого верху" DOM)
                if (parentContainer == null 
                    || parentContainer.Name == "body" 
                    || parentContainer.Name == "html"
                    || parentContainer.HasClass("main-wrapper")) {
                    throw new InvalidOperationException($"[Error]: Header found, but details for group {GroupTag} not found in the container.");
                }
            }

            // Витягнення всього тексту зі знайденого контейнера
            string containerText = parentContainer!.InnerText;
            // Пошук тексту для групи 5.1
            var match = GroupSearchRegex.Match(containerText);

            // Якщо регулярний вираз знайшов збіг
            if (match.Success) {
                // Очищення від зайвих пробілів, зайвих ком або пробілів у кінці рядка
                string scheduleInfo = WhitespaceRegex.Replace(match.Groups[1].Value.Trim(), " ").TrimEnd(',', ' '); 

                // Формування підсумкового повідомлення (заголовок + графік)
                string fullMessage = $"{headerText}\n{scheduleInfo}"; 

                Console.WriteLine(fullMessage); 
            } else {
                Console.WriteLine($"Could not find schedule details for group {GroupTag}.");
            }
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

            using (var httpClient = new HttpClient()) {
                // Збільшуємо таймаут до 120 секунд, враховуючи латентність проксі-мережі
                httpClient.Timeout = TimeSpan.FromSeconds(120);

                // Встановлюємо стандартний User-Agent для ідентифікації клієнта
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                try {
                    // Виконання асинхронного GET-запиту через проксі-шлюз
                    string html = await httpClient.GetStringAsync(proxyUrl);

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
        }
    }
}