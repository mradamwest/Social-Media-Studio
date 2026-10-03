# ChatGPT MCP deployment

Social Media Studio keeps social-network credentials inside the desktop application. The MCP surface must expose only the narrow tools implemented by McpToolService.

## Supported deployment paths

### Private/local deployment
Run the MCP server on the Windows machine and connect it to supported OpenAI products through Secure MCP Tunnel. This keeps the MCP endpoint off the public Internet while allowing OpenAI products to reach it.

### Remote deployment
Deploy the MCP server at a stable HTTPS endpoint using Streamable HTTP. Protect private/write tools with MCP authorization/OAuth.

## Transport requirements
- Prefer Streamable HTTP for production.
- Do not expose SecureTokenStore, provider access tokens, refresh tokens, client secrets, or authorization headers.
- Read and write tools remain distinct.
- Publishing, editing, scheduling, and deletion must route through McpToolService.
- Require user approval for consequential write actions in the ChatGPT/OpenAI client when supported.
- Restrict imported tools to the minimum required set.

## Current implementation status
The desktop publishing facade, persistent queue, scheduled executor, and publishing history are implemented. Network MCP transport and authentication are the next implementation layer.

## ChatGPT availability note
Full custom MCP write/modify actions in ChatGPT depend on the user's ChatGPT plan/workspace capabilities. A local MCP server is not directly reachable by hosted ChatGPT; use Secure MCP Tunnel or a remote HTTPS deployment.
