using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace BlackoutBuster {
    internal static class Program {
        // Адреса сторінки з графіками Запоріжжяобленерго
        private static readonly string TargetUrl = "https://www.zoe.com.ua/outage/";
        // Ідентифікатор черги (групи) для пошуку
        private static readonly string GroupTag = "5.1";

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
                Console.WriteLine("Could not find a valid schedule header on the page.");
                return;
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
                    Console.WriteLine($"[Error]: Header found, but details for group {GroupTag} not found in the container.");
                    return;
                }
            }

            // Витягнення всього тексту зі знайденого контейнера
            string containerText = parentContainer.InnerText;
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
            string html = string.Empty;
            using (var httpClient = new HttpClient()) {
                // Додавання заголовка User-Agent, щоб сайт не заблокував запит як бота
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                // Отримання HTML-коду сторінки за посиланням
                html = await httpClient.GetStringAsync(TargetUrl); 
            }

            if (string.IsNullOrEmpty(html)) {
                Console.WriteLine("The page was downloaded but the content is empty.");
            }

            HtmlDocument doc = new HtmlDocument();
            // Завантаження HTML та перетворення спецсимволів (наприклад, &nbsp; у пробіли)
            doc.LoadHtml(HtmlEntity.DeEntitize(html)); 
            return doc;
        }
    }
}