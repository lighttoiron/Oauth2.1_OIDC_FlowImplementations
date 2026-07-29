import './sign-in-options.js';
import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/session-status.css');

// The session-status element checks our current session status, then displays to the user the result of our sign in attempt
class SessionStatus extends HTMLElement {
    // observedAttributes is a defined static getter that tells the browser which attribute changes should lead to calling attributeChangedCallback
    // Registering 'mode' here ensures that whenever 'mode' is changed, we can check the user's session status and render appropriately
    static get observedAttributes() { return ['mode']; };

    // Called whenever an observedAttribute is changed
    // Can be populated with parameters, i.e. attributeChangedCallback(name, oldValue, newValue) - values can be null
    attributeChangedCallback() {
        // If we are connected to the document, re-render whenever the mode attribute changes
        // We need to check this.isConnected because attributeChangedCallback can be called before the shadow DOM is set up
        // this.shadowRoot also does not ensure that the element has been added to the live DOM, but this.isConnected ensures we are set up and on the page
        if (this.isConnected) this.checkSession();
    }

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

        this._onSignedIn = () => this.checkSession();
        this._onSignInError = (e) => {
            this.shadowRoot.innerHTML = `
                <p class="error">Sign in failed: ${e.detail.error}</p>
            `;
        };

        this.addEventListener('signed-in', this._onSignedIn);
        this.addEventListener('sign-in-error', this._onSignInError);
    }

    disconnectedCallback() {
        this.removeEventListener('signed-in', this._onSignedIn);
        this.removeEventListener('sign-in-error', this._onSignInError);
    }

    async checkSession() {
        const response = await fetch('/bff/me');
        const data = await response.json();

        // Clear the innerHTML so we can either disappear or offer sign in buttons
        this.shadowRoot.innerHTML = '';

        // If the user is signed in, dispatch the session-ready event, otherwise offer sign in buttons
        if (data.authenticated) {
            this.dispatchEvent(new CustomEvent('session-ready', {
                bubbles: true, // Lets objects other than this object receive this event
                composed: true, // Lets listeners that exist outside this element's shadow DOM receive this event
                detail: {
                    subject: data.subject,
                    hasApiAccess: data.hasApiAccess,
                    hasRefreshToken: data.hasRefreshToken
                }
            }));
        } else {
            const signInElement = document.createElement('sign-in-options');
            signInElement.setAttribute('login-type', this.getAttribute('login-type' || 'full'));
            this.shadowRoot.appendChild(signInElement);
        }
    }
}

customElements.define('session-status', SessionStatus);