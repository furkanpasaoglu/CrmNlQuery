# vLLM Provider

Status: Implemented.

## Summary
A second local model server, selected by configuration. Ollama remains the default. This feature does not change the default; it makes switching possible on hardware where a batching server makes sense.

## Behavior
- `Llm:Provider=Vllm` selects `VllmClient`, which calls the OpenAI-compatible `POST {BaseUrl}/v1/chat/completions`. vLLM is not treated as cloud (`Llm:AllowCloud` is not required). For data-protection reasons, a non-local address is accepted only with `Vllm:AllowRemoteHost=true`, intended for a GPU server inside the organization's own network.
- Structured output uses `response_format = {type: json_schema, json_schema: {name, strict, schema}}`.
- Thinking is controlled by `chat_template_kwargs.enable_thinking`; if the setting is empty nothing is sent. With thinking enabled the server must run with `--reasoning-parser qwen3` so reasoning goes to a separate field and `content` stays clean JSON. A leading `<think>...</think>` in the reply is stripped anyway.
- Token counts come from `usage.prompt_tokens` and `usage.completion_tokens`.
- Context overflow: a 400 message that reports the prompt exceeded the maximum context length (wording varies by version) becomes `PromptTooLongException` (cause prompt); `finish_reason=length`, or prompt plus output at or above `Vllm:MaxModelLen`, becomes cause generation. `Ollama:NumCtx` does not apply to vLLM; the window is the server's `--max-model-len`.
- Each pipeline step gets its own client through `LlmClientFactory.CreateForStep`. Metering records the provider as "vLLM". The health check is `GET /v1/models`.
- Eval: `--provider vllm` (model from `Vllm:Model` or `--model`).

## Settings
| Key | Meaning |
|---|---|
| `Vllm:BaseUrl` | Default `http://localhost:8000` |
| `Vllm:Model` | Required; the server's `--served-model-name` |
| `Vllm:ApiKey` | Used if the server is protected with `--api-key`; never logged |
| `Vllm:Think`, `ThinkAnalyze`, `ThinkDecompose`, `ThinkPlan`, `ThinkCreate` | Same rules as the Ollama settings; `create` defaults to no thinking |
| `Vllm:MaxModelLen` | The server's `--max-model-len`, used for overflow detection |
| `Vllm:TimeoutMinutes` | Default 10 |
| `Vllm:AllowRemoteHost` | Required for non-local hosts |

## Limitations
Knowledge-base embeddings (bge-m3) still come from Ollama. A deployment that runs only vLLM needs Ollama for embeddings as well; an OpenAI-compatible embedding client is not implemented.

## Verification
Unit tests use a fake server (request shape, API key, error mapping). A real server can be used to check the protocol only: the schema is honored, thinking can be turned off, the overflow message is recognized and token counts arrive. A live test class, `LiveVllmTests`, runs against a running server. Speed and quality comparisons with Ollama are measured on the target GPU (`performance-and-load-measurement.md`).
