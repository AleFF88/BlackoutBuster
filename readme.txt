# BlackoutBuster

**BlackoutBuster** — This is a .NET-based tool for automatically monitoring hourly power outage schedules in the **Zaporizhzhia Oblast**. The program extracts data from the official **Zaporizhzhyaoblenergo** website and sends notifications to Telegram when changes are detected.

## ### Key Features
* **Web Scraping via ScraperAPI**: Uses a proxy service with Ukrainian geo-location to bypass restrictions and ensure stable access.
* **Targeted Monitoring**: Configured to monitor a specific group (queue) — default is **5.1** (hardcoded).
* **Smart Change Detection**: Stores the state in `state.json` and sends a message only when the date or the schedule itself is updated.
* **Telegram Integration**: Delivery of the current schedule to a specified Telegram channel.

## ### Prerequisites
The following environment variables must be configured for the application to work:
* `SCRAPER_API_KEY`: ScraperAPI proxy access key.
* `TELEGRAM_BOT_TOKEN`: Telegram bot's token.

## ### Technical Details
* **Parser**: Uses `HtmlAgilityPack` for DOM tree navigation.
* **Regex**: Uses regular expressions to extract precise time intervals for the group, even if the text is minified.
* **JSON**: `System.Text.Json` is used for state management with Unicode support for Cyrillic characters.

## ### Usage
The app runs one check cycle at startup:
1. **Downloads HTML**: Fetches the page via a proxy.
2. **Finds Header**: Searches for the schedule heading.
3. **Extracts Schedule**: Isolates the text for the appropriate group using a lookahead regex.
4. **Compares State**: Checks if the newly fetched data differs from the last saved state.
5. **Notifies & Saves**: Sends a Telegram message if an update is found and updates `state.json`.

## ### Automation with GitHub Actions
To avoid the need for a local server, this tool is fully automated using **GitHub Actions**.
* **Scheduled Execution**: The workflow is configured to run every 2 hours starting from 07:00 Kyiv time (05:00 UTC).
* **State Persistence**: After each run, the action checks for changes in `state.json`. If the schedule has been updated, the bot automatically commits and pushes the new state back to the repository.
* **CI/CD Integration**:
* **Workflow Triggers**: Supports both scheduled runs and manual triggers via `workflow_dispatch`.
* **Environment Secrets**: Sensitive data like `SCRAPER_API_KEY` and `TELEGRAM_BOT_TOKEN` are securely stored in GitHub Secrets.
* **Error Handling**: The application is designed to return **Exit Code 1** upon any critical parsing or network error. This allows GitHub Actions to mark the run as "Failed" and notify the developer if the website format changes.

## ### GitHub Actions Workflow Configuration
The workflow includes the following steps:
1. **Checkout**: Pulls the latest code and the current `state.json`.
2. **Setup .NET**: Prepares the environment with the .NET 9.0 SDK.
3. **Execution**: Runs the scraper with the necessary environment variables.
4. **Git Commit & Push**: If the schedule for Group 5.1 has changed, the bot performs a commit with the message `Update schedule state [skip ci]` to avoid infinite loops.