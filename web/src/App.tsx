import { Link, Route, Routes } from 'react-router';
import DiscussionListPage from './pages/DiscussionListPage';
import DiscussionPage from './pages/DiscussionPage';
import TopicPage from './pages/TopicPage';
import TopicsPage from './pages/TopicsPage';

export default function App() {
  return (
    <div className="app">
      <header className="app-header">
        <Link to="/" className="app-name">
          The Discussion
        </Link>
      </header>
      <main id="main" className="app-main">
        <Routes>
          <Route path="/" element={<DiscussionListPage />} />
          <Route path="/discussions/:id" element={<DiscussionPage />} />
          <Route path="/topics" element={<TopicsPage />} />
          <Route path="/topics/:tag" element={<TopicPage />} />
          <Route
            path="*"
            element={
              <>
                <h1>Page not found</h1>
                <p>
                  <Link to="/">Back to all discussions</Link>
                </p>
              </>
            }
          />
        </Routes>
      </main>
    </div>
  );
}
