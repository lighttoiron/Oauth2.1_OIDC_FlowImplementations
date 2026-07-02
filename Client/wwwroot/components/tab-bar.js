class TabBar extends HTMLElement {
    // observedAttributes is a predefined part of Web Components, forming a pair with attributeChangedCallback to allow listening to these attributes
    static get observedAttributes() {
        return ['active'];
    }

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.render();
        this.shadowRoot.addEventListener('click', (e) => {
            // e.target is the actual element that was clicked, .closest() walks up the DOM looking for the nearest 'data-tab' parent, or null if none found
            const tab = e.target.closest('[data-tab');
            if (!tab) {
                return;
            }

            this.dispatchEvent(new CustomEvent('tab-change', {
                bubbles: true,
                composed: true,
                detail: {tab: tab.dataset.tab }
            }));
        });
    }

    attributeChangedCallback() {
        this.render();
    }

    render() {
        const active = this.getAttribute('active') || 'oidc';
        const tabs = [
            { id: 'oidc', label: 'OIDC Login Flow' },
            { id: 'oauth', label: 'OAuth2.0 API Access' }
        ];

        // Note: :host will access the style of the tab-bar tag itself, this is the proper way to style it from within the shadow root
        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; }
                nav {
                    display: flex;
                    gap: 2px;
                    border-bottom: 1px solid #2A2D3A;
                    padding: 0 24px;
                }
                button {
                    position: relative;
                    background: none;
                    border: none;
                    color: #8E8EA0;
                    font-family: 'Inter', sans-serif;
                    font-size: 13px;
                    font-weight: 500;
                    letter-spacing: 0.02em;
                    padding: 14px 18px 12px;
                    cursor: pointer;
                    transition: color 0.15s;
                    outline: none;
                }
                    button:hover { color: #E0E0F0 }
                    button.active { color: #E0E0F0 }
                    button.active::after {
                        content: '';
                        position: absolute;
                        bottom: -1px;
                        left: 0; right: 0;
                        height: 2px;
                        background: #7C6AFE;
                        border-radius: 2px 2px 0 0;
                        animation: pulse-in 0.25s ease;
                    }
                    @keyframes pulse-in {
                        from { opacity: 0; transform: scaleX(0.4); }
                        to   { opacity: 1; transform: scaleX(1); }
                    }
                    @media (prefers-reduced-motion: reduce) {
                        button.active::after { animation:none; }
                    }
            </style>
            <nav>
                ${tabs.map(tab => `
                    <button data-tab="${tab.id}" class="${tab.id === active ? 'active' : ''}>
                        ${tab.label}
                    </button>`).join('')}                    
            </nav>
        `;
    }
}

customElements.define('tab-bar', TabBar);