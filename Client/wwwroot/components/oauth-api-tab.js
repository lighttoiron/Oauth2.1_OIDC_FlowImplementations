import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/oauth-api-tab.css');

class OauthApiTab extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <p class="flow-label">Bearer Token - Resource Server</p>
            <p class="description">
                <strong>Accesses a Protected Resource</strong> from an external API using OAuth2.0 Bearer access tokens.
                The BFF server attaches the token server-side before forwarding the request to the protected API.
                Token is retrieved using the Oauth2.0 Authorization Code flow with PKCE using a BFF SPA architecture.
            </p>
            <div class="placeholder">Oauth2.0 API access panel - coming soon!</div>
        `;
    }
}

customElements.define('oauth-api-tab', OauthApiTab);