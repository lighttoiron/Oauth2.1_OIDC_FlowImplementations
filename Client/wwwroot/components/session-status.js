// We should import these JS files to ensure they are parsed before this file (since we use them as a part of this component)
// For our app, since the user has to interact before they would be rendered, we wouldn't really need this, but it is good practice
// And allows us to not include a script tag in our page HTML for any component not explicitly loaded there
import './sign-in-options.js';
import './api-caller.js';
import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/session-status.css');

// The Session Status element checks our current session status, then displays to the user the result of our sign in attempt
// If the user is signed in, offer a call to the protected API
// If the user is not signed in, offer sign in options
class SessionStatus extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <p class="loading">Loading Session...</p>
        `
        this.checkSession();

        this.addEventListener('signed-in', () => this.checkSession());
        this.addEventListener('sign-in-error', (event) => {
            this.shadowRoot.innerHTML = `
                <p class="error">Sign in failed: ${event.detail.error}</p>
            `;
        });
    }

    async checkSession() {
        const response = await fetch('/bff/me');
        const data = await response.json();

        this.shadowRoot.innerHTML = '';

        if (data.authenticated) {
            const bar = document.createElement('div');
            bar.innerHTML = `
                <span>
                    <span class="indicator"></span>
                    <span class="subject">${data.subject}</span>
                </span>
            `;
            this.shadowRoot.appendChild(bar);
            this.shadowRoot.appendChild(document.createElement('api-caller'));
        } else {
            this.shadowRoot.appendChild(document.createElement('sign-in-options'));
        }
    }
}

customElements.define('session-status', SessionStatus);