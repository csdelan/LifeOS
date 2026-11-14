/** The consent page's mock: a pretend request from Claude, for mock mode and the e2e paths. */
import type { ConsentClient } from "@/lib/oauth-consent";

/** An id the mock treats as expired, to show that state. */
export const MOCK_EXPIRED_ID = "mock-expired";

const MOCK_CALLBACK = "https://claude.ai/api/mcp/auth_callback";

/** A pretend request from Claude, for mock mode and the e2e paths. */
export function mockConsentClient(): ConsentClient {
  return {
    async lookup(authorizationId) {
      if (authorizationId === MOCK_EXPIRED_ID) {
        throw new Error("authorization not found");
      }
      return {
        kind: "ask",
        request: {
          authorizationId,
          // Claude connecting again: the mock list already has its earlier client, "mock-claude".
          clientId: "mock-claude-2",
          clientName: "Claude",
          clientUri: "https://claude.ai",
          redirectUri: MOCK_CALLBACK,
          scopes: ["openid", "email", "profile"],
          email: "chris@example.com",
        },
      };
    },
    async approve() {
      return `${MOCK_CALLBACK}?code=mock-code&state=mock-state`;
    },
    async deny() {
      return `${MOCK_CALLBACK}?error=access_denied&state=mock-state`;
    },
  };
}
