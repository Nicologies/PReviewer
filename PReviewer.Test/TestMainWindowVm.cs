﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExtendedCL;
using NSubstitute;
using NUnit.Framework;
using Octokit;
using PReviewer.Domain;
using PReviewer.Model;

namespace PReviewer.Test
{
    [TestFixture]
    public class TestMainWindowVm
    {
        private readonly PullRequestLocator _pullRequestLocator = new PullRequestLocator
        {
            Repository = "repo",
            Owner = "owner",
            PullRequestNumber = new Random().Next()
        };

        private IRepositoryCommitsClient _commitsClient;
        private MockCompareResult _compareResults;
        private IRepositoryContentsClient _contentsClient;
        private IDiffToolLauncher _diffTool;
        private IFileContentPersist _fileContentPersist;
        private IGitHubClient _gitHubClient;
        private MainWindowVm _mainWindowVm;
        private IPullRequestsClient _prClient;
        private MockPullRequest _pullRequest;
        private IRepositoriesClient _repoClient;
        private IPatchService _patchService;
        private string _baseFileName;
        private string _headFileName;

        [SetUp]
        public void SetUp()
        {
            _compareResults = new MockCompareResult();
            _gitHubClient = Substitute.For<IGitHubClient>();
            _repoClient = Substitute.For<IRepositoriesClient>();
            _commitsClient = Substitute.For<IRepositoryCommitsClient>();
            _prClient = Substitute.For<IPullRequestsClient>();
            _contentsClient = Substitute.For<IRepositoryContentsClient>();
            _fileContentPersist = Substitute.For<IFileContentPersist>();
            _diffTool = Substitute.For<IDiffToolLauncher>();
            _patchService = Substitute.For<IPatchService>();
            _gitHubClient.Repository.Returns(_repoClient);
            _repoClient.Commits.Returns(_commitsClient);
            _repoClient.PullRequest.Returns(_prClient);
            _repoClient.Content.Returns(_contentsClient);

            _commitsClient.Compare(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
                ).Returns(Task.FromResult((CompareResult) _compareResults));

            _mainWindowVm = new MainWindowVm(_gitHubClient, _fileContentPersist, _diffTool, _patchService)
            {
                PullRequestLocator = _pullRequestLocator,
                IsUrlMode = false
            };

            _pullRequest = new MockPullRequest();
            _prClient.Get(_mainWindowVm.PullRequestLocator.Owner, _mainWindowVm.PullRequestLocator.Repository,
                _mainWindowVm.PullRequestLocator.PullRequestNumber).Returns(Task.FromResult((PullRequest) _pullRequest));

            _baseFileName = MainWindowVm.BuildBaseFileName(_pullRequest.Base.Sha, _compareResults.File1.Filename);
            _headFileName = MainWindowVm.BuildHeadFileName(_pullRequest.Head.Sha, _compareResults.File1.Filename);
        }

        [Test]
        public async void ShouldBeAbleToGetDiffsForPullRequest()
        {
            await _mainWindowVm.RetrieveDiffs();

            _prClient.Received(1)
                .Get(_mainWindowVm.PullRequestLocator.Owner, _mainWindowVm.PullRequestLocator.Repository,
                    _mainWindowVm.PullRequestLocator.PullRequestNumber).IgnoreAsyncWarning();
            _commitsClient.Received(1)
                .Compare(_mainWindowVm.PullRequestLocator.Owner, _mainWindowVm.PullRequestLocator.Repository,
                    _pullRequest.Base.Sha, _pullRequest.Head.Sha).IgnoreAsyncWarning();
            Assert.That(_mainWindowVm.Diffs, Contains.Item(_compareResults.File1));
            Assert.That(_mainWindowVm.Diffs, Contains.Item(_compareResults.File2));
        }

        [Test]
        public async void ShouldUpdateBusyStatusProperly()
        {
            var updateCount = 0;
            _mainWindowVm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == PropertyName.Get((MainWindowVm x) => x.IsProcessing))
                {
                    updateCount++;
                }
            };
            await _mainWindowVm.RetrieveDiffs();

            Assert.That(updateCount, Is.EqualTo(2));
            Assert.False(_mainWindowVm.IsProcessing);
        }

        [Test]
        public void GivenAnException_BusyStatusShouldBeReset()
        {
            _prClient.When(x => x.Get(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>()))
                .Do(x => { throw new Exception(); });

            Assert.Throws<Exception>(async () => await _mainWindowVm.RetrieveDiffs());

            Assert.False(_mainWindowVm.IsProcessing);
        }

        [Test]
        public void ShouldBeInUrlModeByDefault()
        {
            Assert.True(new MainWindowVm(_gitHubClient, _fileContentPersist, _diffTool, _patchService).IsUrlMode);
        }

        [Test]
        public async void ShouldUpdatePRLocator_WhenRetrieveChangesInUrlMode()
        {
            _mainWindowVm.IsUrlMode = true;
            _mainWindowVm.PullRequestUrl = string.Format(@"https://github.com/{0}/{1}/pull/{2}",
                _pullRequestLocator.Owner,
                _pullRequestLocator.Repository,
                _pullRequestLocator.PullRequestNumber);
            _mainWindowVm.PullRequestLocator.Owner = "AnotherOwner";
            _mainWindowVm.PullRequestLocator.Repository = "AnotherRepo";
            _mainWindowVm.PullRequestLocator.PullRequestNumber = _pullRequestLocator.PullRequestNumber + 11;

            await _mainWindowVm.RetrieveDiffs();
            Assert.That(_mainWindowVm.PullRequestLocator.Owner, Is.EqualTo(_pullRequestLocator.Owner));
            Assert.That(_mainWindowVm.PullRequestLocator.Repository, Is.EqualTo(_pullRequestLocator.Repository));
            Assert.That(_mainWindowVm.PullRequestLocator.PullRequestNumber,
                Is.EqualTo(_pullRequestLocator.PullRequestNumber));
        }

        [Test]
#pragma warning disable 1998
        public async void GivenAnInvalidUrl_ShouldThrowAnException()
#pragma warning restore 1998
        {
            _mainWindowVm.IsUrlMode = true;
            _mainWindowVm.PullRequestUrl = "";
            Assert.Throws<UriFormatException>(async () => await _mainWindowVm.RetrieveDiffs());

            _mainWindowVm.PullRequestUrl = "asl;dfkjasldf";
            Assert.Throws<UriFormatException>(async () => await _mainWindowVm.RetrieveDiffs());
        }

        [Test]
        public async void ShouldStoreBaseCommitAndHeadCommit()
        {
            Assert.IsNullOrEmpty(_mainWindowVm.BaseCommit);
            Assert.IsNullOrEmpty(_mainWindowVm.HeadCommit);
            await _mainWindowVm.RetrieveDiffs();
            Assert.That(_mainWindowVm.BaseCommit, Is.EqualTo(_pullRequest.Base.Sha));
            Assert.That(_mainWindowVm.HeadCommit, Is.EqualTo(_pullRequest.Head.Sha));
        }

        [Test]
        public async void CanRetieveFileContent()
        {
            MockFile1PersistFor("baseContent", _pullRequest.Base.Sha);
            MockFile1PersistFor("headContent", _pullRequest.Head.Sha);

            _mainWindowVm.SelectedDiffFile = _compareResults.File1;

            await _mainWindowVm.RetrieveDiffs();

            await _mainWindowVm.PrepareDiffContent();

            var basePath = _compareResults.File1.GetFilePath(_pullRequest.Base.Sha);
            var headPath = _compareResults.File1.GetFilePath(_pullRequest.Head.Sha);

            _contentsClient.Received(1).GetContents(_pullRequestLocator.Owner, _pullRequestLocator.Repository,
                basePath).IgnoreAsyncWarning();

            _contentsClient.Received(1).GetContents(_pullRequestLocator.Owner, _pullRequestLocator.Repository,
                headPath).IgnoreAsyncWarning();
        }

        [Test]
        public async void BusyStatusSetCorretly_WhenRetrieveFileContent()
        {
            MockFile1PersistFor("baseContent", _pullRequest.Base.Sha);
            MockFile1PersistFor("headContent", _pullRequest.Head.Sha);

            _mainWindowVm.SelectedDiffFile = _compareResults.File1;
            await _mainWindowVm.RetrieveDiffs();

            var updateCount = 0;
            _mainWindowVm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == PropertyName.Get((MainWindowVm x) => x.IsProcessing))
                {
                    updateCount++;
                }
            };

            await _mainWindowVm.PrepareDiffContent();

            Assert.That(updateCount, Is.EqualTo(2));
            Assert.False(_mainWindowVm.IsProcessing);
        }

        [Test]
        public async void BusyStatusSetCorretly_WhenFailedToGetContent()
        {
            _mainWindowVm.SelectedDiffFile = _compareResults.File1;
            await _mainWindowVm.RetrieveDiffs();

            _contentsClient.When(x => x.GetContents(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>())).Do(x => { throw new Exception(); });

            Assert.Throws<Exception>(async () => await _mainWindowVm.PrepareDiffContent());

            Assert.False(_mainWindowVm.IsProcessing);
        }

        [Test]
        public async void SaveToTempDir_WhenFileContentRecieved()
        {
            var baseContent = MockFile1PersistFor("baseContent", _pullRequest.Base.Sha);

            var headContent = MockFile1PersistFor("headContent", _pullRequest.Head.Sha);

            _mainWindowVm.SelectedDiffFile = _compareResults.File1;

            await _mainWindowVm.RetrieveDiffs();

            await _mainWindowVm.PrepareDiffContent();

            _fileContentPersist.Received(1).SaveContent(_pullRequestLocator,
                _headFileName,
                headContent.Content).IgnoreAsyncWarning();
            _fileContentPersist.Received(1).SaveContent(_pullRequestLocator,
                _baseFileName,
                baseContent.Content).IgnoreAsyncWarning();
        }

        [Test]
        public async void CanCallDiffTool()
        {
            var baseContent = MockFile1PersistFor("baseContent", _pullRequest.Base.Sha);
            var headContent = MockFile1PersistFor("headContent", _pullRequest.Head.Sha);

            const string basePath = "basepath";
            _fileContentPersist.SaveContent(Arg.Any<PullRequestLocator>(),
                Arg.Any<string>(),
                baseContent.Content).Returns(Task.FromResult(basePath));
            const string headPath = "headpath";
            _fileContentPersist.SaveContent(Arg.Any<PullRequestLocator>(),
                Arg.Any<string>(),
                headContent.Content).Returns(Task.FromResult(headPath));

            _mainWindowVm.SelectedDiffFile = _compareResults.File1;

            await _mainWindowVm.RetrieveDiffs();

            await _mainWindowVm.PrepareDiffContent();

            _diffTool.Received(1).Open(basePath, headPath);
        }

        [Test]
        public async void AbleToCachedFiles()
        {
            _fileContentPersist.ExistsInCached(Arg.Any<PullRequestLocator>(),
                _baseFileName).Returns(true);
            _fileContentPersist.ExistsInCached(Arg.Any<PullRequestLocator>(),
                _headFileName).Returns(true);
            const string cachedPath = "DummyPath";
            _fileContentPersist.GetCachedFilePath(Arg.Any<PullRequestLocator>(), Arg.Any<string>()).Returns(cachedPath);

            _mainWindowVm.SelectedDiffFile = _compareResults.File1;

            await _mainWindowVm.RetrieveDiffs();

            await _mainWindowVm.PrepareDiffContent();

            _contentsClient.DidNotReceiveWithAnyArgs().GetContents("", "", "").IgnoreAsyncWarning();
            _fileContentPersist.DidNotReceiveWithAnyArgs().SaveContent(null, "", "").IgnoreAsyncWarning();
            _fileContentPersist.Received(1).GetCachedFilePath(_pullRequestLocator, _baseFileName);
            _fileContentPersist.Received(1).GetCachedFilePath(_pullRequestLocator, _headFileName);
            _diffTool.Received(1).Open(cachedPath, cachedPath);
        }

        [Test]
        public async void GivenANewAddedFile_ShouldProvideAFakeBaseFile()
        {
            var headContent = MockFile1PersistFor("headContent", _pullRequest.Head.Sha);

            const string basePath = "basepath";
            _fileContentPersist.SaveContent(Arg.Any<PullRequestLocator>(),
                Arg.Any<string>(),
                "").Returns(Task.FromResult(basePath));

            const string headPath = "headpath";
            _fileContentPersist.SaveContent(Arg.Any<PullRequestLocator>(),
                Arg.Any<string>(),
                headContent.Content).Returns(Task.FromResult(headPath));

            _compareResults.File1.Status = GitFileStatus.New;
            _mainWindowVm.SelectedDiffFile = _compareResults.File1;

            await _mainWindowVm.RetrieveDiffs();

            await _mainWindowVm.PrepareDiffContent();
            
            _contentsClient.DidNotReceive().GetContents(_pullRequestLocator.Owner,
                _pullRequestLocator.Repository, _baseFileName).IgnoreAsyncWarning();
            _diffTool.Received(1).Open(basePath, headPath);
        }

        [Test]
        public async void GivenARenamedFile_ShouldCallPatchToGetBaseFile()
        {
            _patchService.RevertViaPatch(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(""));

            var headContent = MockFile1PersistFor("headContent", _pullRequest.Head.Sha);

            const string basePath = "basePath";
            _fileContentPersist.GetCachedFilePath(_pullRequestLocator, _baseFileName).Returns(basePath);

            const string headPath = "headpath";
            _fileContentPersist.SaveContent(Arg.Any<PullRequestLocator>(),
                Arg.Any<string>(),
                headContent.Content).Returns(Task.FromResult(headPath));

            _fileContentPersist.ReadContent(headPath).Returns(Task.FromResult(headContent.Content));

            _compareResults.File1.Status = GitFileStatus.Renamed;
            _mainWindowVm.SelectedDiffFile = _compareResults.File1;

            await _mainWindowVm.RetrieveDiffs();

            await _mainWindowVm.PrepareDiffContent();

            _patchService.Received(1)
                .RevertViaPatch(headContent.Content, _compareResults.File1.Patch, basePath)
                .IgnoreAsyncWarning();

            _fileContentPersist.DidNotReceive().SaveContent(Arg.Any<PullRequestLocator>(),
                _baseFileName,
                Arg.Any<string>()).IgnoreAsyncWarning();

            _contentsClient.DidNotReceive().GetContents(_pullRequestLocator.Owner,
                _pullRequestLocator.Repository, _baseFileName).IgnoreAsyncWarning();
            _diffTool.Received(1).Open(basePath, headPath);
        }

        private MockRepositoryContent MockFile1PersistFor(string rawContent, string sha)
        {
            var headContent = new MockRepositoryContent {EncodedContent = rawContent};
            IReadOnlyList<RepositoryContent> headContentCollection =
                new List<RepositoryContent> {headContent}.AsReadOnly();
            _contentsClient.GetContents(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<string>(x => x == _compareResults.File1.GetFilePath(sha)))
                .Returns(Task.FromResult(headContentCollection));
            return headContent;
        }
    }

    internal class MockRepositoryContent : RepositoryContent
    {
        public new string EncodedContent
        {
            set { base.EncodedContent = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes((value))); }
        }
    }

    internal class MockPullRequest : PullRequest
    {
        public MockPullRequest()
        {
            Base = new GitReference("", "", "", "1212", null, null);
            Head = new GitReference("", "", "", "asdfasdf", null, null);
        }
    }

    internal class MockGitHubCommit : GitHubCommit
    {
        public new string Sha
        {
            get { return base.Sha; }
            set { base.Sha = value; }
        }
    }

    internal class MockGitHubCommitFile : GitHubCommitFile
    {
        public new string Sha
        {
            get { return base.Sha; }
            set { base.Sha = value; }
        }

        public new string Filename
        {
            get { return base.Filename; }
            set { base.Filename = value; }
        }

        public new string Status
        {
            get { return base.Status; }
            set { base.Status = value; }
        }

        public new string Patch
        {
            get { return base.Patch; }
            set { base.Patch = value; }
        }
    }

    internal class MockCompareResult : CompareResult
    {
        public MockGitHubCommitFile File1 = new MockGitHubCommitFile
        {
            Sha = "e74fe8d371a5e33c4877f662e6f8ed7c0949a8b0",
            Filename = "test.xaml",
            Patch = "Patch",
        };

        public MockGitHubCommitFile File2 = new MockGitHubCommitFile
        {
            Sha = "9dc7f01526e368a64c49714c51f1d851885793ba",
            Filename = "app.xaml.cs"
        };

        public MockCompareResult()
        {
            Files = new List<GitHubCommitFile>
            {
                File1,
                File2
            };
            var mockBasCommit = new MockGitHubCommit {Sha = "ef4f2857776d06cc28acedbd023bbb33ca83d216"};
            BaseCommit = mockBasCommit;
        }
    }
}