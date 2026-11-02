# Slash Commands

Status: Implemented.

## Summary
The chat widget offers a small command menu opened by typing `/`. It lets an analyst do frequent actions from the keyboard and lets a new user discover what to ask. It is a widget-only feature; the server is unchanged. No command reaches the model, counts against a quota, or is saved as a question.

## Commands
Command names are fixed English; descriptions come from the widget's catalog.

| Command | Action | Available when |
|---|---|---|
| `/help` | Adds a temporary help message to the chat flow: the command list plus example questions (clicking one fills the input). Not saved; disappears when the chat changes. | Always |
| `/new` | Starts a new empty chat. | A chat with content is open |
| `/edit` | Puts the last question of this chat into the input and focuses it; sending it creates a new turn (the old turn is kept). | At least one question was asked in this chat |
| `/history` | Opens the left chat list if collapsed and focuses its search box. | Always |
| `/clear` | Opens the existing "delete all chat history" flow: counts from the server, a confirmation, a "delete pinned too" box. | Chats exist; otherwise the existing "nothing to delete" notice |

`/clear` uses the existing confirmation unchanged. Its note is accurate: CRM records are not affected, only chat history; when "Open in CRM" is on, deleting all history also removes the user's temporary Open in CRM view.

## Menu behavior
- The menu opens only when the input text is exactly `/` followed by letters, no spaces (`/^\/[a-zA-Z]*$/`). "1/2", "10/2026", "a /b" and "/new something" do not open it.
- It filters by command-name prefix as the user types. With no match it shows one line, "No such command. Type /help."; Enter then does nothing and the text is not sent to the model.
- Keyboard: Up/Down select, Enter runs, Tab completes the name, Esc closes the menu (text stays). Pointer: clicking a row runs it.
- The menu opens above the input inside the widget's shadow DOM with `role="listbox"` and `aria-activedescendant`.
- While a question is running the menu does not open and no command runs.
- The input is cleared after a command runs.
- Each command has an `available()` condition (for example `/edit` is hidden when there is no last question). The server has no permission concept for these commands: everyone manages only their own history.

## `/help` content
The command list comes from the catalog. Example questions come from the same source as the empty-chat examples: the server's `exampleQuestions` (`Widget:ExampleQuestions`, set at install time in the customer's own tables and language), falling back to neutral built-in examples. Generating examples from the schema is out of scope.

## No model, no record
Commands never call the ask path: no `/api/ask` request, no quota counter, no metering or access log, nothing written to history. The help message exists only in the DOM. `/clear` uses only the existing delete endpoints.

## Texts
All new texts live in the widget catalog (`cmdHelp`, `cmdNew`, `cmdEdit`, `cmdHistory`, `cmdClear`, `noSuchCommand`, `helpTitle`, `helpExamples`, `helpCommands`) in both languages.

## Design notes
Pure functions are unit-testable: `parseSlash(text)` returns `{ open, query }`; `filterCommands(list, query, state)` filters by prefix and `available()` while keeping order. The `lastQuestion` state is fed when an answer arrives and when a chat is opened (from its last message); a stopped question does not become the last question.

## Out of scope
Server-side commands, user-defined commands, keyboard shortcuts, extra commands such as `/settings`, schema-generated example questions.
