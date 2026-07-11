import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/user-info.css');

class UserInfo extends HTMLElement {
    set subjectInfo(value) {
        this._subject = value.subject;
        this._hasApiAccess = value.hasApiAccess;
        this._hasRefreshToken = value.hasRefreshToken;
        this.render();
    }

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        if (this._subject) {
            this.render();
        }
    }

    render() {
        if (!this._subject) return;

        this.shadowRoot.innerHTML = `
            <div>
                <span class="indicator"></span>
                <span class="subject">Username: ${this._subject} </span>
                <span class="subject">API Access: ${this._hasApiAccess} </span>
                <span class="subject">Refresh Token: ${this._hasRefreshToken}</span>
            </div>
        `;
    }
}

customElements.define('user-info', UserInfo);